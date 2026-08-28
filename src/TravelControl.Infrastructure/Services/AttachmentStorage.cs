using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;

namespace TravelControl.Infrastructure.Services;

public sealed record StoredAttachment(Attachment Entity, bool Duplicate);

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

    public async Task<StoredAttachment> SaveAsync(IFormFile file, DocumentType documentType, Guid userId, string? description,
        Guid? passengerId, Guid? roomId, Guid? flightId, Guid? baggageId, CancellationToken ct)
    {
        if (file.Length is <= 0 || file.Length > maxBytes) throw new InvalidOperationException($"El archivo debe pesar entre 1 byte y {maxBytes / 1024 / 1024} MB.");
        if (!Signatures.TryGetValue(file.ContentType.ToLowerInvariant(), out var allowed)) throw new InvalidOperationException("Solo se permiten PDF, PNG y JPEG.");
        await using var memory = new MemoryStream(); await file.CopyToAsync(memory, ct); var bytes = memory.ToArray();
        if (!allowed.Any(signature => bytes.AsSpan().StartsWith(signature))) throw new InvalidOperationException("El contenido real no coincide con el tipo MIME.");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var duplicate = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Sha256 == hash, ct);
        if (duplicate is not null) return new(duplicate, true);
        Directory.CreateDirectory(root);
        var extension = file.ContentType.ToLowerInvariant() switch { "application/pdf" => ".pdf", "image/png" => ".png", _ => ".jpg" };
        var storedName = $"{Guid.NewGuid():N}{extension}"; var path = Path.Combine(root, storedName);
        await File.WriteAllBytesAsync(path, bytes, ct);
        var entity = new Attachment { DocumentType = documentType, OriginalName = Path.GetFileName(file.FileName), StoredName = storedName,
            MimeType = file.ContentType.ToLowerInvariant(), Size = file.Length, SecurePath = path, Sha256 = hash, UploadedById = userId,
            Description = description, PassengerId = passengerId, RoomReservationId = roomId, FlightBookingId = flightId, BaggageEntitlementId = baggageId };
        db.Attachments.Add(entity); await db.SaveChangesAsync(ct); return new(entity, false);
    }

    public async Task<(Attachment Entity, Stream Stream)> OpenAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.Attachments.FindAsync([id], ct) ?? throw new KeyNotFoundException();
        var path = Path.GetFullPath(entity.SecurePath);
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException();
        return (entity, File.OpenRead(path));
    }
}
