using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;
using TravelControl.Infrastructure.Services;
using Xunit;

namespace TravelControl.Infrastructure.Tests;

public sealed class TripReadinessTests
{
    [Fact]
    public async Task Global_property_blocker_caps_progress_and_excel_uses_the_same_snapshot_until_resolved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var trip = new Trip
        {
            Name = "Viaje readiness ficticio", Destination = "Destino", IsActive = true,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 10), PassportWarningDays = 30,
            TransferStatus = new TripTransferStatus { IsConfirmed = true }
        };
        var op = new Operator { Name = "Top Travel", Type = OperatorType.Agency };
        var room = new RoomReservation
        {
            Trip = trip, Operator = op, InternalCode = "E2E-ROOM", Status = VerificationStatus.Confirmed,
            Hotel = "Por confirmar", SpecificPropertyPending = true, RoomType = "Doble", ExpectedCapacity = 1,
            CheckIn = trip.StartDate, CheckOut = trip.EndDate, SourceReference = "Referencia ficticia"
        };
        var passenger = new Passenger
        {
            Trip = trip, PrimaryOperator = op, RoomReservation = room, FullName = "Persona readiness",
            NormalizedName = "PERSONA READINESS", BirthDate = new DateOnly(1990, 1, 1), Nationality = "Ficticia",
            PassportNumber = "FIXTURE-001", NormalizedPassportNumber = "FIXTURE-001", PassportExpiry = new DateOnly(2030, 1, 1),
            PassportReviewStatus = VerificationStatus.Confirmed, DocumentationStatus = VerificationStatus.Confirmed
        };
        var flight = new FlightBooking
        {
            Trip = trip, Airline = "Aerolínea ficticia", Pnr = "PNR-FIXTURE", SourceReference = "Referencia ficticia",
            BaggageStatus = VerificationStatus.Confirmed, CheckedBagIncluded = true, CheckedBagCount = 1,
            CheckedBagWeightKg = 23, BaggageAppliesOutbound = true, BaggageAppliesReturn = true,
            Segments =
            [
                new FlightSegment { Type = SegmentType.Outbound, FlightNumber = "FX1", OriginAirport = "AAA", DestinationAirport = "BBB", DepartureAt = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero), ArrivalAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero), Sequence = 1 },
                new FlightSegment { Type = SegmentType.Return, FlightNumber = "FX2", OriginAirport = "BBB", DestinationAirport = "AAA", DepartureAt = new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero), ArrivalAt = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero), Sequence = 2 }
            ]
        };
        flight.PassengerFlights.Add(new PassengerFlight
        {
            Passenger = passenger, ElectronicTicketNumber = "FIXTURE-TICKET", TicketStatus = VerificationStatus.Confirmed,
            TicketAccessStatus = TicketAccessStatus.Verified,
            TicketAccessUrl = "https://mytrips.copaair.com/trip-detail/ABC123/FICTIONAL"
        });
        db.AddRange(trip, op, room, passenger, flight);
        await db.SaveChangesAsync(ct);

        var evidence = new EvidenceResolver(db);
        var passengerQueries = new PassengerQueryService(db, evidence);
        var readiness = new TripReadinessService(db, passengerQueries, evidence);
        var export = new ExcelExportService(db, passengerQueries, evidence, readiness);

        var blocked = await readiness.GetAsync(ct);
        Assert.Equal(TripOverallStatus.Attention, blocked.OverallStatus);
        Assert.Equal(100, blocked.BaseProgressPercent);
        Assert.Equal(99, blocked.ProgressPercent);
        Assert.Equal(1, blocked.RoomsConfirmed);
        Assert.Equal(1, blocked.SpecificPropertiesPending);
        Assert.Single(blocked.Blockers, x => x.Key == "properties");
        AssertWorkbook(await export.ExportAsync(ct), 99, "Attention", 1, 1);

        room.Hotel = "Hotel ficticio confirmado";
        room.SpecificPropertyPending = false;
        await db.SaveChangesAsync(ct);
        var ready = await readiness.GetAsync(ct);
        Assert.Equal(TripOverallStatus.Ready, ready.OverallStatus);
        Assert.Equal(100, ready.ProgressPercent);
        Assert.Empty(ready.Blockers);
        AssertWorkbook(await export.ExportAsync(ct), 100, "Ready", 1, 0);
    }

    private static void AssertWorkbook(byte[] bytes, int progress, string overall, int confirmedRooms, int pendingProperties)
    {
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var dashboard = workbook.Worksheet("Dashboard");
        Assert.Equal(confirmedRooms, dashboard.Cell("B7").GetValue<int>());
        Assert.Equal(pendingProperties, dashboard.Cell("B8").GetValue<int>());
        Assert.Equal(progress, dashboard.Cell("B9").GetValue<int>());
        Assert.Equal(overall, dashboard.Cell("B10").GetString());
        Assert.Equal(1, workbook.Worksheets.SelectMany(sheet => sheet.CellsUsed())
            .Count(cell => cell.GetString() == "Transfer grupal"));
    }
}
