using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;
using Xunit;

namespace TravelControl.Infrastructure.Tests;

public sealed class HardeningMigrationTests
{
    [Fact]
    public async Task Previous_schema_migrates_links_and_tickets_additively_without_changing_physical_metadata()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), $"travel-control-migration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "migration.db");
        var physicalPath = Path.Combine(root, "evidence.pdf");
        await File.WriteAllTextAsync(physicalPath, "%PDF-1.7 fictional", ct);
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={dbPath}").Options;
            await using var db = new AppDbContext(options);
            var migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20260830000425_AddEvidenceLinksAndIdentification", ct);
            var trip = new Trip { Name = "Migration fixture", Destination = "Fictional" };
            var passenger = new Passenger { Trip = trip, FullName = "Migration Person", NormalizedName = "MIGRATION PERSON" };
            var flight = new FlightBooking { Trip = trip, Pnr = "PNR-FIXTURE", Airline = "Fixture Air" };
            var attachment = new Attachment
            {
                DocumentType = DocumentType.AirTicket, OriginalName = "fixture.pdf", StoredName = "stored-fixture.pdf",
                MimeType = "application/pdf", Size = new FileInfo(physicalPath).Length, SecurePath = physicalPath,
                Sha256 = "HASH-MIGRATION-FIXTURE", UploadedById = Guid.NewGuid(), PassengerId = passenger.Id
            };
            var start = "2026-01-01";
            var end = "2026-01-10";
            var wedding = "2026-01-05";
            var timeZone = "UTC";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Trips (Id, Name, Destination, StartDate, EndDate, WeddingDate, TimeZone, IsActive,
                    PassportWarningDays, CreatedAt, UpdatedAt, Version)
                VALUES ({trip.Id}, {trip.Name}, {trip.Destination}, {start}, {end}, {wedding},
                    {timeZone}, 1, 180, {trip.CreatedAt}, {trip.UpdatedAt}, 1)
                """, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Passengers (Id, TripId, FullName, NormalizedName, PassportReviewStatus,
                    DocumentationStatus, CreatedAt, UpdatedAt, Version)
                VALUES ({passenger.Id}, {trip.Id}, {passenger.FullName}, {passenger.NormalizedName}, 1, 1,
                    {passenger.CreatedAt}, {passenger.UpdatedAt}, 1)
                """, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO FlightBookings (Id, TripId, Status, Airline, Pnr, CreatedAt, UpdatedAt, Version)
                VALUES ({flight.Id}, {trip.Id}, 0, {flight.Airline}, {flight.Pnr}, {flight.CreatedAt}, {flight.UpdatedAt}, 1)
                """, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Attachments (Id, DocumentType, OriginalName, StoredName, MimeType, Size, SecurePath,
                    Sha256, UploadedById, UploadedAt, PassengerId, CreatedAt, UpdatedAt, Version)
                VALUES ({attachment.Id}, 1, {attachment.OriginalName}, {attachment.StoredName}, {attachment.MimeType},
                    {attachment.Size}, {attachment.SecurePath}, {attachment.Sha256}, {attachment.UploadedById},
                    {attachment.UploadedAt}, {passenger.Id}, {attachment.CreatedAt}, {attachment.UpdatedAt}, 1)
                """, ct);
            var linkId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO AttachmentLinks (Id, AttachmentId, PassengerId, RoomReservationId, FlightBookingId,
                    BaggageEntitlementId, CreatedByUserId, CreatedAt, UpdatedAt, Version)
                VALUES ({linkId}, {attachment.Id}, {passenger.Id}, NULL, NULL, NULL, {attachment.UploadedById},
                    {attachment.UploadedAt}, {attachment.UploadedAt}, 1)
                """, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO PassengerFlights (PassengerId, FlightBookingId, ElectronicTicketNumber, TicketStatus, Notes)
                VALUES ({passenger.Id}, {flight.Id}, 'TICKET-FIXTURE', 1, 'fixture')
                """, ct);
            var secondPassengerId = Guid.NewGuid();
            var secondName = "Second Migration Person";
            var secondNormalized = "SECOND MIGRATION PERSON";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Passengers (Id, TripId, FullName, NormalizedName, PassportReviewStatus,
                    DocumentationStatus, CreatedAt, UpdatedAt, Version)
                VALUES ({secondPassengerId}, {trip.Id}, {secondName}, {secondNormalized}, 1, 1,
                    {passenger.CreatedAt}, {passenger.UpdatedAt}, 1)
                """, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO BaggageEntitlements (Id, PassengerId, FlightBookingId, Status, CheckedBagCount,
                    WeightPerBagKg, AppliesOutbound, AppliesReturn, CreatedAt, UpdatedAt, Version)
                VALUES ({Guid.NewGuid()}, {passenger.Id}, {flight.Id}, 0, 1, 23, 1, 1,
                    {passenger.CreatedAt}, {passenger.UpdatedAt}, 1),
                       ({Guid.NewGuid()}, {secondPassengerId}, {flight.Id}, 3, 0, 0, 0, 0,
                    {passenger.CreatedAt}, {passenger.UpdatedAt}, 1)
                """, ct);
            var before = (attachment.Id, attachment.Sha256, attachment.SecurePath, attachment.StoredName,
                Attachments: await ScalarAsync(db, "SELECT COUNT(*) FROM Attachments", ct),
                Links: await ScalarAsync(db, "SELECT COUNT(*) FROM AttachmentLinks", ct));

            await migrator.MigrateAsync(cancellationToken: ct);

            Assert.Equal("ok", await TextScalarAsync(db, "PRAGMA integrity_check", ct));
            Assert.Equal(before.Attachments, await ScalarAsync(db, "SELECT COUNT(*) FROM Attachments", ct));
            Assert.Equal(before.Links, await ScalarAsync(db, "SELECT COUNT(*) FROM AttachmentLinks", ct));
            Assert.Equal(before.Id, Guid.Parse(await TextScalarAsync(db, "SELECT Id FROM Attachments", ct)));
            Assert.Equal((int)DocumentType.AirTicket, await ScalarAsync(db,
                "SELECT EvidenceType FROM AttachmentLinks", ct));
            Assert.Equal(1, await ScalarAsync(db, "SELECT Version FROM PassengerFlights", ct));
            Assert.Equal(1, await ScalarAsync(db,
                "SELECT COUNT(*) FROM PassengerFlights WHERE length(PublicTicketAccessToken) >= 43", ct));
            Assert.Equal(1, await ScalarAsync(db,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_PassengerFlights_PublicTicketAccessToken' AND sql LIKE '%UNIQUE%'", ct));
            Assert.Equal((int)VerificationStatus.InProgress, await ScalarAsync(db,
                "SELECT BaggageStatus FROM FlightBookings", ct));
            Assert.Equal(2, await ScalarAsync(db, "SELECT COUNT(*) FROM BaggageEntitlements", ct));
            Assert.Equal(1, await ScalarAsync(db,
                "SELECT COUNT(*) FROM AuditLogs WHERE Action='LegacyBaggageConflict'", ct));
            Assert.Equal(before.Sha256, await TextScalarAsync(db, "SELECT Sha256 FROM Attachments", ct));
            Assert.Equal(before.SecurePath, await TextScalarAsync(db, "SELECT SecurePath FROM Attachments", ct));
            Assert.Equal(before.StoredName, await TextScalarAsync(db, "SELECT StoredName FROM Attachments", ct));
            Assert.True(File.Exists(physicalPath));
            Assert.Equal(2, await ScalarAsync(db,
                "SELECT COUNT(*) FROM pragma_table_info('PassengerFlights') WHERE name IN ('Version','UpdatedAt')", ct));
            Assert.Equal(4, await ScalarAsync(db,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND tbl_name='AttachmentLinks' AND sql LIKE '%EvidenceType%'", ct));
            Assert.Equal(1, await ScalarAsync(db,
                "SELECT COUNT(*) FROM Attachments WHERE Id IS NOT NULL AND DocumentType IS NOT NULL AND OriginalName IS NOT NULL AND StoredName IS NOT NULL AND SecurePath IS NOT NULL AND Sha256 IS NOT NULL", ct));
            Assert.Equal(1, await ScalarAsync(db,
                "SELECT COUNT(*) FROM PassengerFlights WHERE PassengerId IS NOT NULL AND FlightBookingId IS NOT NULL AND ElectronicTicketNumber='TICKET-FIXTURE'", ct));
            await db.DisposeAsync();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task<int> ScalarAsync(AppDbContext db, string sql, CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        if (command.Connection!.State != System.Data.ConnectionState.Open) await command.Connection.OpenAsync(ct);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }
    private static async Task<string> TextScalarAsync(AppDbContext db, string sql, CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        if (command.Connection!.State != System.Data.ConnectionState.Open) await command.Connection.OpenAsync(ct);
        return Convert.ToString(await command.ExecuteScalarAsync(ct))!;
    }
}
