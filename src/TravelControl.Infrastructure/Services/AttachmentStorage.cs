using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;

namespace TravelControl.Infrastructure.Services;

public sealed record StoredAttachment(Attachment Entity, AttachmentLink Link, bool DuplicateFile, bool LinkCreated);

public sealed class AttachmentStorage(AppDbContext db, IConfiguration config)
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
            var (duplicateLink, created) = await LinkCoreAsync(duplicate, userId, passengerId, roomId, flightId, baggageId, ct);
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
        var link = NewLink(entity.Id, userId, passengerId, roomId, flightId, baggageId);
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
        Guid userId,
        Guid? passengerId,
        Guid? roomId,
        Guid? flightId,
        Guid? baggageId,
        CancellationToken ct)
    {
        ValidateTarget(passengerId, roomId, flightId, baggageId);
        await ValidateTargetExistsAsync(passengerId, roomId, flightId, baggageId, ct);
        var attachment = await db.Attachments.SingleOrDefaultAsync(x => x.Id == attachmentId, ct)
            ?? throw new KeyNotFoundException();
        return await LinkCoreAsync(attachment, userId, passengerId, roomId, flightId, baggageId, ct);
    }

    public async Task<(Attachment Entity, Stream Stream)> OpenAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.Attachments.FindAsync([id], ct) ?? throw new KeyNotFoundException();
        var path = Path.GetFullPath(entity.SecurePath);
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException();
        return (entity, File.OpenRead(path));
    }

    private async Task<(AttachmentLink Link, bool Created)> LinkCoreAsync(
        Attachment attachment,
        Guid userId,
        Guid? passengerId,
        Guid? roomId,
        Guid? flightId,
        Guid? baggageId,
        CancellationToken ct)
    {
        var existing = await db.AttachmentLinks.SingleOrDefaultAsync(x => x.AttachmentId == attachment.Id
            && x.PassengerId == passengerId && x.RoomReservationId == roomId
            && x.FlightBookingId == flightId && x.BaggageEntitlementId == baggageId, ct);
        if (existing is not null) return (existing, false);
        var link = NewLink(attachment.Id, userId, passengerId, roomId, flightId, baggageId);
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
                && x.FlightBookingId == flightId && x.BaggageEntitlementId == baggageId, ct);
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

    private static AttachmentLink NewLink(Guid attachmentId, Guid userId, Guid? passengerId, Guid? roomId, Guid? flightId, Guid? baggageId) => new()
    {
        AttachmentId = attachmentId,
        PassengerId = passengerId,
        RoomReservationId = roomId,
        FlightBookingId = flightId,
        BaggageEntitlementId = baggageId,
        CreatedByUserId = userId
    };

    private static void ValidateTarget(Guid? passengerId, Guid? roomId, Guid? flightId, Guid? baggageId)
    {
        var count = new[] { passengerId, roomId, flightId, baggageId }.Count(x => x.HasValue);
        if (count != 1) throw new InvalidOperationException("Cada vínculo debe apuntar exactamente a una entidad.");
    }
}
