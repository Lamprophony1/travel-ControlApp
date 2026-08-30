using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;
using Xunit;

namespace TravelControl.Infrastructure.Tests;

public sealed class MigrationTests
{
    [Fact]
    public async Task Additive_migration_backfills_legacy_attachment_links_without_changing_files_or_counts()
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"migration-fixture-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options;
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Guid attachmentId;
            Guid passengerId;
            await using (var before = new AppDbContext(options))
            {
                await before.Database.MigrateAsync("20260828193955_InitialSqlite", ct);
                var trip = new Trip { Name = "Viaje de migración", Destination = "Destino", IsActive = true };
                var op = new Operator { Name = "Operadora de migración", Type = OperatorType.Agency };
                var room = new RoomReservation { Trip = trip, Operator = op, InternalCode = "MIG-01", ExpectedCapacity = 1 };
                var passenger = new Passenger { Trip = trip, RoomReservation = room, FullName = "Persona migración", NormalizedName = "PERSONA MIGRACION" };
                var attachment = new Attachment
                {
                    DocumentType = DocumentType.AirTicket, OriginalName = "legacy.pdf", StoredName = "immutable.pdf",
                    MimeType = "application/pdf", Size = 123, SecurePath = "/fixture/immutable.pdf", Sha256 = "LEGACY-HASH",
                    UploadedById = Guid.NewGuid(), PassengerId = passenger.Id
                };
                before.AddRange(trip, op, room, passenger, attachment);
                await before.SaveChangesAsync(ct);
                attachmentId = attachment.Id;
                passengerId = passenger.Id;
            }

            await using (var after = new AppDbContext(options))
            {
                await after.Database.MigrateAsync(ct);
                await after.Database.OpenConnectionAsync(ct);
                await using var integrityCommand = after.Database.GetDbConnection().CreateCommand();
                integrityCommand.CommandText = "PRAGMA integrity_check;";
                Assert.Equal("ok", await integrityCommand.ExecuteScalarAsync(ct));
                Assert.Equal(1, await after.Passengers.CountAsync(ct));
                Assert.Equal(1, await after.RoomReservations.CountAsync(ct));
                Assert.Equal(1, await after.Attachments.CountAsync(ct));
                var attachment = await after.Attachments.SingleAsync(x => x.Id == attachmentId, ct);
                Assert.Equal("/fixture/immutable.pdf", attachment.SecurePath);
                Assert.Equal("immutable.pdf", attachment.StoredName);
                Assert.Equal("LEGACY-HASH", attachment.Sha256);
                var link = await after.AttachmentLinks.SingleAsync(ct);
                Assert.Equal(attachmentId, link.AttachmentId);
                Assert.Equal(passengerId, link.PassengerId);
                Assert.Null(link.RoomReservationId);
                Assert.Null(link.FlightBookingId);
                Assert.Null(link.BaggageEntitlementId);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
            if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
        }
    }
}
