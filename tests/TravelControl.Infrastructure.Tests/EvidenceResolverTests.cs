using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
        Assert.Contains(related, item => item.DocumentType == DocumentType.AirTicket && item.Source == "FlightBooking");
        Assert.Contains(related, item => item.DocumentType == DocumentType.HotelVoucher && item.Source == "RoomReservation");
        Assert.Contains(related, item => item.DocumentType == DocumentType.BaggageProof && item.Source == "FlightBooking");
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
            var storage = new AttachmentStorage(fixture.Db, configuration);
            var bytes = "%PDF-1.7\nfictional evidence"u8.ToArray();
            var first = await storage.SaveAsync(FormFileFrom(bytes), DocumentType.AirTicket, fixture.UserId, null,
                fixture.FirstPassenger.Id, null, null, null, fixture.Ct);
            var second = await storage.SaveAsync(FormFileFrom(bytes), DocumentType.AirTicket, fixture.UserId, null,
                fixture.SecondPassenger.Id, null, null, null, fixture.Ct);

            Assert.False(first.DuplicateFile);
            Assert.True(second.DuplicateFile);
            Assert.True(second.LinkCreated);
            Assert.Equal(first.Entity.Id, second.Entity.Id);
            Assert.Equal(1, await fixture.Db.Attachments.CountAsync(fixture.Ct));
            Assert.Equal(2, await fixture.Db.AttachmentLinks.CountAsync(fixture.Ct));

            fixture.Db.AttachmentLinks.Remove(first.Link);
            await fixture.Db.SaveChangesAsync(fixture.Ct);
            Assert.Equal(1, await fixture.Db.Attachments.CountAsync(fixture.Ct));
            Assert.Equal(1, await fixture.Db.AttachmentLinks.CountAsync(fixture.Ct));
            Assert.True(File.Exists(first.Entity.SecurePath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static FormFile FormFileFrom(byte[] bytes)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", "evidence.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
    }

    private sealed class EvidenceFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AppDbContext Db { get; }
        public Passenger FirstPassenger { get; }
        public Passenger SecondPassenger { get; }
        public Passenger UnrelatedPassenger { get; }
        public Guid UserId { get; } = Guid.NewGuid();
        public CancellationToken Ct => TestContext.Current.CancellationToken;

        private EvidenceFixture(SqliteConnection connection, AppDbContext db, Passenger first, Passenger second, Passenger unrelated)
        {
            _connection = connection; Db = db; FirstPassenger = first; SecondPassenger = second; UnrelatedPassenger = unrelated;
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
                ticket.Links.Add(new AttachmentLink { FlightBookingId = flight.Id, CreatedByUserId = userId });
                voucher.Links.Add(new AttachmentLink { RoomReservationId = room.Id, CreatedByUserId = userId });
                baggageProof.Links.Add(new AttachmentLink { FlightBookingId = flight.Id, CreatedByUserId = userId });
                db.Attachments.AddRange(ticket, voucher, baggageProof);
                await db.SaveChangesAsync(ct);
            }
            return new EvidenceFixture(connection, db, first, second, unrelated);
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
