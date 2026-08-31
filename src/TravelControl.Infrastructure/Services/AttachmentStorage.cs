using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.Json;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;

namespace TravelControl.Infrastructure.Services;

public sealed record StoredAttachment(Attachment Entity, AttachmentLink Link, bool DuplicateFile, bool LinkCreated);
public sealed record AttachmentLinkImpact(string SourceType, string SourceLabel, int AffectedPassengerCount,
    int AffectedEntityCount, bool IsShared, bool CanUnlink, bool CanDeleteIfOrphan);
public sealed record AttachmentUnlinkResult(bool LinkRemoved, bool AttachmentDeleted, bool StillShared, int RemainingLinks);

public sealed class AttachmentStorage(AppDbContext db, IConfiguration config, ILogger<AttachmentStorage> logger)
{
    private static readonly Dictionary<string, byte[][]> Signatures = new()
    {
        ["application/pdf"] = [Convert.FromHexString("25504446")],
        ["image/png"] = [Convert.FromHexString("89504E470D0A1A0A")],
        ["image/jpeg"] = [[0xFF, 0xD8, 0xFF]]
    };
    private readonly string root = Path.GetFullPath(config["Storage:Root"] ?? "/var/lib/travel-control/attachments");
    private readonly long maxBytes = config.GetValue<long?>("Storage:MaxBytes") ?? 10 * 1024 * 1024;

    public async Task<StoredAttachment> SaveAsync(
        IFormFile file,
        DocumentType documentType,
        Guid userId,
        string? description,
        Guid? passengerId,
        Guid? roomId,
        Guid? flightId,
        Guid? baggageId,
        CancellationToken ct)
    {
        ValidateTarget(passengerId, roomId, flightId, baggageId);
        ValidateEvidenceTarget(documentType, flightId, baggageId);
        await ValidateTargetExistsAsync(passengerId, roomId, flightId, baggageId, ct);
        if (file.Length is <= 0 || file.Length > maxBytes)
            throw new InvalidOperationException($"El archivo debe pesar entre 1 byte y {maxBytes / 1024 / 1024} MB.");
        var mimeType = file.ContentType.ToLowerInvariant();
        if (!Signatures.TryGetValue(mimeType, out var allowed))
            throw new InvalidOperationException("Solo se permiten PDF, PNG y JPEG.");

        await using var memory = new MemoryStream();
        await file.CopyToAsync(memory, ct);
        var bytes = memory.ToArray();
        if (!allowed.Any(signature => bytes.AsSpan().StartsWith(signature)))
            throw new InvalidOperationException("El contenido real no coincide con el tipo MIME.");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var duplicate = await db.Attachments.FirstOrDefaultAsync(x => x.Sha256 == hash, ct);
        if (duplicate is not null)
        {
            var (duplicateLink, created) = await LinkCoreAsync(duplicate, documentType, userId, passengerId, roomId, flightId, baggageId, ct);
            return new(duplicate, duplicateLink, true, created);
        }

        Directory.CreateDirectory(root);
        var extension = mimeType switch { "application/pdf" => ".pdf", "image/png" => ".png", _ => ".jpg" };
        var storedName = $"{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(root, storedName);
        await File.WriteAllBytesAsync(path, bytes, ct);
        var entity = new Attachment
        {
            DocumentType = documentType,
            OriginalName = Path.GetFileName(file.FileName),
            StoredName = storedName,
            MimeType = mimeType,
            Size = file.Length,
            SecurePath = path,
            Sha256 = hash,
            UploadedById = userId,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()
        };
        var link = NewLink(entity.Id, documentType, userId, passengerId, roomId, flightId, baggageId);
        entity.Links.Add(link);
        db.Attachments.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
            return new(entity, link, false, true);
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    public async Task<(AttachmentLink Link, bool Created)> LinkAsync(
        Guid attachmentId,
        DocumentType evidenceType,
        Guid userId,
        Guid? passengerId,
        Guid? roomId,
        Guid? flightId,
        Guid? baggageId,
        CancellationToken ct)
    {
        ValidateTarget(passengerId, roomId, flightId, baggageId);
        ValidateEvidenceTarget(evidenceType, flightId, baggageId);
        await ValidateTargetExistsAsync(passengerId, roomId, flightId, baggageId, ct);
        var attachment = await db.Attachments.SingleOrDefaultAsync(x => x.Id == attachmentId, ct)
            ?? throw new KeyNotFoundException();
        return await LinkCoreAsync(attachment, evidenceType, userId, passengerId, roomId, flightId, baggageId, ct);
    }

    public async Task<(Attachment Entity, Stream Stream)> OpenAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.Attachments.FindAsync([id], ct) ?? throw new KeyNotFoundException();
        var path = Path.GetFullPath(entity.SecurePath);
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException();
        return (entity, File.OpenRead(path));
    }

    public async Task<AttachmentLinkImpact> GetImpactAsync(Guid attachmentId, Guid linkId, CancellationToken ct)
    {
        var link = await db.AttachmentLinks
            .Include(x => x.Attachment).ThenInclude(x => x.Links)
            .Include(x => x.FlightBooking).ThenInclude(x => x!.PassengerFlights)
            .Include(x => x.RoomReservation).ThenInclude(x => x!.Passengers)
            .Include(x => x.BaggageEntitlement)
            .SingleOrDefaultAsync(x => x.AttachmentId == attachmentId && x.Id == linkId, ct)
            ?? throw new KeyNotFoundException();
        var (sourceType, sourceLabel, affected) = DescribeImpact(link);
        return new(sourceType, sourceLabel, affected, 1, affected > 1, true, link.Attachment.Links.Count == 1);
    }

    public async Task<AttachmentUnlinkResult> UnlinkAsync(
        Guid attachmentId, Guid linkId, bool deleteIfOrphan, Guid userId, string? userName, CancellationToken ct)
    {
        var link = await db.AttachmentLinks.Include(x => x.Attachment).ThenInclude(x => x.Links)
            .SingleOrDefaultAsync(x => x.AttachmentId == attachmentId && x.Id == linkId, ct)
            ?? throw new KeyNotFoundException();
        var attachment = link.Attachment;
        var remaining = attachment.Links.Count - 1;
        string? originalPath = null;
        string? quarantinePath = null;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (deleteIfOrphan && remaining == 0)
            {
                originalPath = SafeAttachmentPath(attachment.SecurePath);
                if (File.Exists(originalPath))
                {
                    var quarantine = Path.Combine(root, ".quarantine");
                    Directory.CreateDirectory(quarantine);
                    quarantinePath = Path.Combine(quarantine, $"{attachment.Id:N}-{Guid.NewGuid():N}.quarantine");
                    File.Move(originalPath, quarantinePath);
                }
            }
            ClearMatchingLegacyAssociation(attachment, link);
            db.AttachmentLinks.Remove(link);
            db.AuditLogs.Add(NewAudit(userId, userName, "AttachmentLink", link.Id, link.PassengerId, "Unlink",
                new { link.AttachmentId, link.EvidenceType, link.PassengerId, link.RoomReservationId, link.FlightBookingId, link.BaggageEntitlementId }));
            var deleteAttachment = deleteIfOrphan && remaining == 0;
            if (deleteAttachment)
            {
                db.Attachments.Remove(attachment);
                db.AuditLogs.Add(NewAudit(userId, userName, "Attachment", attachment.Id, null, "DeleteOrphan",
                    new { attachment.MimeType, attachment.Size, attachment.Sha256 }));
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            if (quarantinePath is not null)
            {
                try { File.Delete(quarantinePath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogError(ex, "Could not remove a quarantined attachment after a committed deletion. attachmentId={AttachmentId}", attachmentId);
                }
            }
            return new(true, deleteAttachment, remaining > 0, remaining);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            if (quarantinePath is not null && originalPath is not null && File.Exists(quarantinePath) && !File.Exists(originalPath))
            {
                try { File.Move(quarantinePath, originalPath); }
                catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException)
                {
                    logger.LogCritical(restoreError, "Could not restore an attachment from quarantine after rollback. attachmentId={AttachmentId}", attachmentId);
                }
            }
            throw;
        }
    }

    private async Task<(AttachmentLink Link, bool Created)> LinkCoreAsync(
        Attachment attachment,
        DocumentType evidenceType,
        Guid userId,
        Guid? passengerId,
        Guid? roomId,
        Guid? flightId,
        Guid? baggageId,
        CancellationToken ct)
    {
        var existing = await db.AttachmentLinks.SingleOrDefaultAsync(x => x.AttachmentId == attachment.Id
            && x.PassengerId == passengerId && x.RoomReservationId == roomId
            && x.FlightBookingId == flightId && x.BaggageEntitlementId == baggageId && x.EvidenceType == evidenceType, ct);
        if (existing is not null) return (existing, false);
        var link = NewLink(attachment.Id, evidenceType, userId, passengerId, roomId, flightId, baggageId);
        db.AttachmentLinks.Add(link);
        try
        {
            await db.SaveChangesAsync(ct);
            return (link, true);
        }
        catch (DbUpdateException)
        {
            db.Entry(link).State = EntityState.Detached;
            existing = await db.AttachmentLinks.SingleOrDefaultAsync(x => x.AttachmentId == attachment.Id
                && x.PassengerId == passengerId && x.RoomReservationId == roomId
                && x.FlightBookingId == flightId && x.BaggageEntitlementId == baggageId && x.EvidenceType == evidenceType, ct);
            if (existing is not null) return (existing, false);
            throw;
        }
    }

    private async Task ValidateTargetExistsAsync(Guid? passengerId, Guid? roomId, Guid? flightId, Guid? baggageId, CancellationToken ct)
    {
        var exists = passengerId.HasValue ? await db.Passengers.AnyAsync(x => x.Id == passengerId, ct)
            : roomId.HasValue ? await db.RoomReservations.AnyAsync(x => x.Id == roomId, ct)
            : flightId.HasValue ? await db.FlightBookings.AnyAsync(x => x.Id == flightId, ct)
            : await db.BaggageEntitlements.AnyAsync(x => x.Id == baggageId, ct);
        if (!exists) throw new InvalidOperationException("La entidad destino no existe.");
    }

    private static AttachmentLink NewLink(Guid attachmentId, DocumentType evidenceType, Guid userId, Guid? passengerId, Guid? roomId, Guid? flightId, Guid? baggageId) => new()
    {
        AttachmentId = attachmentId,
        EvidenceType = evidenceType,
        PassengerId = passengerId,
        RoomReservationId = roomId,
        FlightBookingId = flightId,
        BaggageEntitlementId = baggageId,
        CreatedByUserId = userId
    };

    private string SafeAttachmentPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException();
        return fullPath;
    }

    private static void ClearMatchingLegacyAssociation(Attachment attachment, AttachmentLink link)
    {
        if (attachment.PassengerId == link.PassengerId) attachment.PassengerId = null;
        if (attachment.RoomReservationId == link.RoomReservationId) attachment.RoomReservationId = null;
        if (attachment.FlightBookingId == link.FlightBookingId) attachment.FlightBookingId = null;
        if (attachment.BaggageEntitlementId == link.BaggageEntitlementId) attachment.BaggageEntitlementId = null;
    }

    private static (string SourceType, string SourceLabel, int AffectedPassengers) DescribeImpact(AttachmentLink link)
    {
        if (link.PassengerId.HasValue) return ("Passenger", "Vínculo directo al pasajero", 1);
        if (link.FlightBookingId.HasValue)
            return ("FlightBooking", $"PNR {link.FlightBooking?.Pnr ?? "sin código"}", link.FlightBooking?.PassengerFlights.Count ?? 0);
        if (link.RoomReservationId.HasValue)
            return ("RoomReservation", $"Habitación {link.RoomReservation?.InternalCode ?? "sin código"}", link.RoomReservation?.Passengers.Count ?? 0);
        return ("BaggageEntitlement", "Equipaje individual", link.BaggageEntitlement is null ? 0 : 1);
    }

    private static AuditLog NewAudit(Guid userId, string? userName, string entity, object id, Guid? passengerId, string action, object before) => new()
    {
        UserId = userId,
        UserName = userName,
        EntityName = entity,
        EntityId = id.ToString() ?? string.Empty,
        PassengerId = passengerId,
        Action = action,
        PreviousValue = JsonSerializer.Serialize(before)
    };

    private static void ValidateEvidenceTarget(DocumentType evidenceType, Guid? flightId, Guid? baggageId)
    {
        if (evidenceType == DocumentType.BaggageProof && (!flightId.HasValue || baggageId.HasValue))
            throw new InvalidOperationException("Los comprobantes nuevos de equipaje deben vincularse directamente al PNR.");
    }

    private static void ValidateTarget(Guid? passengerId, Guid? roomId, Guid? flightId, Guid? baggageId)
    {
        var count = new[] { passengerId, roomId, flightId, baggageId }.Count(x => x.HasValue);
        if (count != 1) throw new InvalidOperationException("Cada vínculo debe apuntar exactamente a una entidad.");
    }
}
