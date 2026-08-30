using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TravelControl.Application.Services;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;
using TravelControl.Infrastructure.Services;
using Xunit;

namespace TravelControl.Infrastructure.Tests;

public sealed class EvidenceResolverTests
{
    [Fact]
    public async Task Flight_room_and_baggage_evidence_is_inherited_only_through_related_entities()
    {
        await using var fixture = await EvidenceFixture.CreateAsync();
        var resolver = new EvidenceResolver(fixture.Db);
        var evidence = await resolver.GetForPassengersAsync(
            [fixture.FirstPassenger.Id, fixture.SecondPassenger.Id, fixture.UnrelatedPassenger.Id], fixture.Ct);

        Assert.True(evidence[fixture.FirstPassenger.Id].HasAirTicketEvidence);
        Assert.True(evidence[fixture.SecondPassenger.Id].HasAirTicketEvidence);
        Assert.False(evidence[fixture.UnrelatedPassenger.Id].HasAirTicketEvidence);
        Assert.True(evidence[fixture.FirstPassenger.Id].HasHotelVoucherEvidence);
        Assert.False(evidence[fixture.SecondPassenger.Id].HasHotelVoucherEvidence);
        Assert.True(evidence[fixture.FirstPassenger.Id].HasBaggageEvidence);
        Assert.True(evidence[fixture.SecondPassenger.Id].HasBaggageEvidence);

        var related = await resolver.GetPassengerEvidenceAsync(fixture.FirstPassenger.Id, fixture.Ct);
        Assert.Contains(related, item => item.EvidenceType == DocumentType.AirTicket && item.SourceType == "FlightBooking");
        Assert.Contains(related, item => item.EvidenceType == DocumentType.HotelVoucher && item.SourceType == "RoomReservation");
        Assert.Contains(related, item => item.EvidenceType == DocumentType.BaggageProof && item.SourceType == "FlightBooking");
        Assert.All(related.Where(x => x.SourceType == "FlightBooking"), item =>
        {
            Assert.False(item.IsDirect);
            Assert.False(item.CanUnlinkHere);
            Assert.Equal(2, item.AffectedPassengerCount);
        });
    }

    [Fact]
    public async Task Duplicate_upload_reuses_one_physical_record_and_creates_a_new_link()
    {
        await using var fixture = await EvidenceFixture.CreateAsync(seedEvidence: false);
        var root = Path.Combine(AppContext.BaseDirectory, $"attachment-fixture-{Guid.NewGuid():N}");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Root"] = root,
                ["Storage:MaxBytes"] = "1048576"
            }).Build();
            var storage = new AttachmentStorage(fixture.Db, configuration, NullLogger<AttachmentStorage>.Instance);
            var bytes = "%PDF-1.7\nfictional evidence"u8.ToArray();
            var first = await storage.SaveAsync(FormFileFrom(bytes), DocumentType.AirTicket, fixture.UserId, null,
                fixture.FirstPassenger.Id, null, null, null, fixture.Ct);
            var second = await storage.SaveAsync(FormFileFrom(bytes), DocumentType.AirTicket, fixture.UserId, null,
                fixture.SecondPassenger.Id, null, null, null, fixture.Ct);
            var baggage = await storage.SaveAsync(FormFileFrom(bytes), DocumentType.BaggageProof, fixture.UserId, null,
                null, null, fixture.FlightId, null, fixture.Ct);
            var (flightTicket, flightTicketCreated) = await storage.LinkAsync(first.Entity.Id, DocumentType.AirTicket,
                fixture.UserId, null, null, fixture.FlightId, null, fixture.Ct);

            Assert.False(first.DuplicateFile);
            Assert.True(second.DuplicateFile);
            Assert.True(second.LinkCreated);
            Assert.Equal(first.Entity.Id, second.Entity.Id);
            Assert.Equal(1, await fixture.Db.Attachments.CountAsync(fixture.Ct));
            Assert.True(baggage.DuplicateFile);
            Assert.True(flightTicketCreated);
            Assert.Equal(4, await fixture.Db.AttachmentLinks.CountAsync(fixture.Ct));
            Assert.Equal(2, await fixture.Db.AttachmentLinks.Where(x => x.AttachmentId == first.Entity.Id)
                .Select(x => x.EvidenceType).Distinct().CountAsync(fixture.Ct));

            var impact = await storage.GetImpactAsync(first.Entity.Id, baggage.Link.Id, fixture.Ct);
            Assert.Equal("FlightBooking", impact.SourceType);
            Assert.Equal(2, impact.AffectedPassengerCount);
            await storage.UnlinkAsync(first.Entity.Id, first.Link.Id, false, fixture.UserId, "fixture", fixture.Ct);
            Assert.Equal(1, await fixture.Db.Attachments.CountAsync(fixture.Ct));
            Assert.Equal(3, await fixture.Db.AttachmentLinks.CountAsync(fixture.Ct));
            Assert.True(File.Exists(first.Entity.SecurePath));
            await storage.UnlinkAsync(first.Entity.Id, second.Link.Id, false, fixture.UserId, "fixture", fixture.Ct);
            await storage.UnlinkAsync(first.Entity.Id, flightTicket.Id, false, fixture.UserId, "fixture", fixture.Ct);
            var deleted = await storage.UnlinkAsync(first.Entity.Id, baggage.Link.Id, true, fixture.UserId, "fixture", fixture.Ct);
            Assert.True(deleted.AttachmentDeleted);
            Assert.Equal(0, await fixture.Db.Attachments.CountAsync(fixture.Ct));
            Assert.False(File.Exists(first.Entity.SecurePath));
            Assert.Equal(5, await fixture.Db.AuditLogs.CountAsync(fixture.Ct));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Link_only_unlink_preserves_an_orphan_for_explicit_administrative_cleanup()
    {
        await using var fixture = await EvidenceFixture.CreateAsync(seedEvidence: false);
        var root = Path.Combine(AppContext.BaseDirectory, $"attachment-orphan-{Guid.NewGuid():N}");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Root"] = root, ["Storage:MaxBytes"] = "1048576"
            }).Build();
            var storage = new AttachmentStorage(fixture.Db, configuration, NullLogger<AttachmentStorage>.Instance);
            var stored = await storage.SaveAsync(FormFileFrom("%PDF-1.7\nlink only"u8.ToArray()), DocumentType.Other,
                fixture.UserId, null, fixture.FirstPassenger.Id, null, null, null, fixture.Ct);
            var result = await storage.UnlinkAsync(stored.Entity.Id, stored.Link.Id, false, fixture.UserId, "fixture", fixture.Ct);
            Assert.False(result.AttachmentDeleted);
            Assert.Equal(0, await fixture.Db.AttachmentLinks.CountAsync(fixture.Ct));
            Assert.Equal(1, await fixture.Db.Attachments.CountAsync(fixture.Ct));
            Assert.True(File.Exists(stored.Entity.SecurePath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Sqlite_failure_rolls_back_the_link_and_restores_the_file_from_quarantine()
    {
        await using var fixture = await EvidenceFixture.CreateAsync(seedEvidence: false);
        var root = Path.Combine(AppContext.BaseDirectory, $"attachment-rollback-{Guid.NewGuid():N}");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Root"] = root, ["Storage:MaxBytes"] = "1048576"
            }).Build();
            var storage = new AttachmentStorage(fixture.Db, configuration, NullLogger<AttachmentStorage>.Instance);
            var stored = await storage.SaveAsync(FormFileFrom("%PDF-1.7\nrollback"u8.ToArray()), DocumentType.Other,
                fixture.UserId, null, fixture.FirstPassenger.Id, null, null, null, fixture.Ct);
            await fixture.Db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER fixture_fail_unlink BEFORE DELETE ON AttachmentLinks
                BEGIN SELECT RAISE(ABORT, 'fixture transaction failure'); END;
                """, fixture.Ct);

            await Assert.ThrowsAnyAsync<Exception>(() => storage.UnlinkAsync(stored.Entity.Id, stored.Link.Id, true,
                fixture.UserId, "fixture", fixture.Ct));

            fixture.Db.ChangeTracker.Clear();
            Assert.True(File.Exists(stored.Entity.SecurePath));
            Assert.Equal(1, await fixture.Db.Attachments.CountAsync(fixture.Ct));
            Assert.Equal(1, await fixture.Db.AttachmentLinks.CountAsync(fixture.Ct));
            Assert.Equal("ok", await SqliteIntegrityAsync(fixture.Db, fixture.Ct));
            var quarantine = Path.Combine(root, ".quarantine");
            Assert.False(Directory.Exists(quarantine) && Directory.EnumerateFiles(quarantine).Any());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Unsafe_filesystem_path_fails_before_database_changes()
    {
        await using var fixture = await EvidenceFixture.CreateAsync(seedEvidence: false);
        var root = Path.Combine(AppContext.BaseDirectory, $"attachment-path-safety-{Guid.NewGuid():N}");
        var outside = Path.Combine(AppContext.BaseDirectory, $"outside-{Guid.NewGuid():N}.pdf");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Root"] = root, ["Storage:MaxBytes"] = "1048576"
            }).Build();
            var storage = new AttachmentStorage(fixture.Db, configuration, NullLogger<AttachmentStorage>.Instance);
            var stored = await storage.SaveAsync(FormFileFrom("%PDF-1.7\npath safety"u8.ToArray()), DocumentType.Other,
                fixture.UserId, null, fixture.FirstPassenger.Id, null, null, null, fixture.Ct);
            await File.WriteAllTextAsync(outside, "%PDF-1.7 fictional outside", fixture.Ct);
            stored.Entity.SecurePath = outside;
            await fixture.Db.SaveChangesAsync(fixture.Ct);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => storage.UnlinkAsync(stored.Entity.Id, stored.Link.Id,
                true, fixture.UserId, "fixture", fixture.Ct));

            fixture.Db.ChangeTracker.Clear();
            Assert.True(File.Exists(outside));
            Assert.Equal(1, await fixture.Db.Attachments.CountAsync(fixture.Ct));
            Assert.Equal(1, await fixture.Db.AttachmentLinks.CountAsync(fixture.Ct));
            Assert.Equal("ok", await SqliteIntegrityAsync(fixture.Db, fixture.Ct));
        }
        finally
        {
            if (File.Exists(outside)) File.Delete(outside);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static FormFile FormFileFrom(byte[] bytes)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", "evidence.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
    }

    private static async Task<string> SqliteIntegrityAsync(AppDbContext db, CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        return Convert.ToString(await command.ExecuteScalarAsync(ct))!;
    }

    private sealed class EvidenceFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AppDbContext Db { get; }
        public Passenger FirstPassenger { get; }
        public Passenger SecondPassenger { get; }
        public Passenger UnrelatedPassenger { get; }
        public Guid FlightId { get; }
        public Guid UserId { get; } = Guid.NewGuid();
        public CancellationToken Ct => TestContext.Current.CancellationToken;

        private EvidenceFixture(SqliteConnection connection, AppDbContext db, Passenger first, Passenger second, Passenger unrelated, Guid flightId)
        {
            _connection = connection; Db = db; FirstPassenger = first; SecondPassenger = second; UnrelatedPassenger = unrelated; FlightId = flightId;
        }

        public static async Task<EvidenceFixture> CreateAsync(bool seedEvidence = true)
        {
            var ct = TestContext.Current.CancellationToken;
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(ct);
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync(ct);
            var trip = new Trip { Name = "Viaje ficticio", Destination = "Destino", IsActive = true };
            var op = new Operator { Name = "Operadora ficticia", Type = OperatorType.Agency };
            var room = new RoomReservation
            {
                Trip = trip, InternalCode = "ROOM-1", Operator = op, Status = VerificationStatus.Confirmed,
                RoomType = "Doble", CheckIn = new DateOnly(2026, 9, 1), CheckOut = new DateOnly(2026, 9, 5), ExpectedCapacity = 2
            };
            var first = new Passenger { Trip = trip, FullName = "Persona Uno", NormalizedName = "PERSONA UNO", RoomReservation = room };
            var second = new Passenger { Trip = trip, FullName = "Persona Dos", NormalizedName = "PERSONA DOS" };
            var unrelated = new Passenger { Trip = trip, FullName = "Persona Tres", NormalizedName = "PERSONA TRES" };
            var flight = new FlightBooking { Trip = trip, Airline = "Aerolínea ficticia", Pnr = "PNR-ONE" };
            flight.PassengerFlights.Add(new PassengerFlight { Passenger = first });
            flight.PassengerFlights.Add(new PassengerFlight { Passenger = second });
            var otherFlight = new FlightBooking { Trip = trip, Airline = "Otra aerolínea", Pnr = "PNR-OTHER" };
            otherFlight.PassengerFlights.Add(new PassengerFlight { Passenger = unrelated });
            var baggage = new BaggageEntitlement { Passenger = first, FlightBooking = flight };
            db.AddRange(trip, op, room, first, second, unrelated, flight, otherFlight, baggage);
            await db.SaveChangesAsync(ct);

            if (seedEvidence)
            {
                var userId = Guid.NewGuid();
                var ticket = Attachment("ticket.pdf", "HASH-TICKET", DocumentType.AirTicket, userId);
                var voucher = Attachment("voucher.pdf", "HASH-VOUCHER", DocumentType.HotelVoucher, userId);
                var baggageProof = Attachment("baggage.pdf", "HASH-BAGGAGE", DocumentType.BaggageProof, userId);
                ticket.Links.Add(new AttachmentLink { FlightBookingId = flight.Id, EvidenceType = DocumentType.AirTicket, CreatedByUserId = userId });
                voucher.Links.Add(new AttachmentLink { RoomReservationId = room.Id, EvidenceType = DocumentType.HotelVoucher, CreatedByUserId = userId });
                baggageProof.Links.Add(new AttachmentLink { FlightBookingId = flight.Id, EvidenceType = DocumentType.BaggageProof, CreatedByUserId = userId });
                db.Attachments.AddRange(ticket, voucher, baggageProof);
                await db.SaveChangesAsync(ct);
            }
            return new EvidenceFixture(connection, db, first, second, unrelated, flight.Id);
        }

        private static Attachment Attachment(string name, string hash, DocumentType type, Guid userId) => new()
        {
            DocumentType = type, OriginalName = name, StoredName = name, MimeType = "application/pdf", Size = 10,
            SecurePath = Path.Combine(AppContext.BaseDirectory, name), Sha256 = hash, UploadedById = userId
        };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
