using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using System.Text;
using TravelControl.Application.Services;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;
using TravelControl.Infrastructure.Services;
using Xunit;

namespace TravelControl.Infrastructure.Tests;

public sealed class PassengerTravelManifestImportTests
{
    [Fact]
    public async Task Forty_six_row_manifest_is_safe_grouped_and_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.CreateAsync(46, 25);
        var csv = BuildManifest(46);
        var service = fixture.Service();

        var preview = await service.PreviewAsync(Stream(csv), "manifest.csv", true, true, null, ct);

        Assert.True(preview.CanCommit);
        Assert.Equal(46, preview.RowsRead);
        Assert.Equal(46, preview.MatchedPassengers);
        Assert.Equal(42, preview.SourcePassengersWithPnr);
        Assert.Equal(4, preview.SourcePassengersWithoutPnr);
        Assert.Equal(21, preview.UniquePnrs);
        Assert.Equal(29, preview.AirlinesDetected["Copa Airlines"]);
        Assert.Equal(13, preview.AirlinesDetected["LATAM Airlines"]);
        Assert.Equal(4, preview.AirlinesDetected["Sin aerolínea confirmada"]);
        Assert.Equal(39, preview.PassportsProvided);
        Assert.Equal(40, preview.BirthDatesProvided);
        Assert.Equal(39, preview.PassportExpiriesProvided);
        Assert.Equal(46, preview.NationalitiesProvided);
        Assert.Equal(46, preview.IgnoredSecondaryDates);
        Assert.Equal(0, preview.BlockingErrors);

        var committed = await service.CommitAsync(Stream(csv), "manifest.csv", preview.Sha256, true, true, true,
            null, Guid.NewGuid(), "fixture-admin", ct);

        Assert.NotNull(committed.ImportRunId);
        Assert.Equal(21, committed.PnrsCreated);
        Assert.Equal(42, committed.AssociationsCreated);
        Assert.Equal(39, committed.PassportsUpdated);
        Assert.Equal(40, committed.BirthDatesUpdated);
        Assert.Equal(39, committed.PassportExpiriesUpdated);
        Assert.Equal(46, committed.NationalitiesUpdated);
        Assert.Equal(46, await fixture.Db.Passengers.CountAsync(ct));
        Assert.Equal(25, await fixture.Db.RoomReservations.CountAsync(ct));
        Assert.Equal(21, await fixture.Db.FlightBookings.CountAsync(ct));
        Assert.Equal(42, await fixture.Db.PassengerFlights.CountAsync(x => x.TicketStatus == VerificationStatus.Confirmed, ct));
        Assert.Equal(4, await fixture.Db.Passengers.CountAsync(x => !x.PassengerFlights.Any(), ct));
        Assert.False(await fixture.Db.PassengerFlights.AnyAsync(x => x.ElectronicTicketNumber != null, ct));
        Assert.False(await fixture.Db.FlightSegments.AnyAsync(ct));
        Assert.False(await fixture.Db.Passengers.AnyAsync(x => x.DocumentationStatus != VerificationStatus.ToVerify, ct));
        Assert.Equal(25, await fixture.Db.RoomReservations.CountAsync(x => x.CheckIn == new DateOnly(2026, 8, 1)
            && x.CheckOut == new DateOnly(2026, 8, 8), ct));

        var secondPreview = await service.PreviewAsync(Stream(csv), "manifest.csv", true, true, null, ct);
        var second = await service.CommitAsync(Stream(csv), "manifest.csv", secondPreview.Sha256, true, true, true,
            null, Guid.NewGuid(), "fixture-admin", ct);

        Assert.Equal(0, second.PnrsCreated);
        Assert.Equal(0, second.AssociationsCreated);
        Assert.Equal(0, second.PassportsUpdated);
        Assert.Equal(0, second.BirthDatesUpdated);
        Assert.Equal(0, second.PassportExpiriesUpdated);
        Assert.Equal(0, second.NationalitiesUpdated);
        Assert.Equal(46, await fixture.Db.Passengers.CountAsync(ct));
        Assert.Equal(25, await fixture.Db.RoomReservations.CountAsync(ct));
    }

    [Fact]
    public async Task Suggested_alias_is_never_applied_without_explicit_selection()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.CreateAsync(1, 0, "Persona Ficticia Extendida");
        var csv = SingleRow("Persona Ficticia", "FIXTURE1", "CM", "PX001");
        var service = fixture.Service();

        var preview = await service.PreviewAsync(Stream(csv), "manifest.csv", true, true, null, ct);

        Assert.False(preview.CanCommit);
        var suggestion = Assert.Single(preview.SuggestedMatches);
        Assert.DoesNotContain("Persona", suggestion.MaskedCandidate, StringComparison.OrdinalIgnoreCase);

        var confirmed = await service.PreviewAsync(Stream(csv), "manifest.csv", true, true,
            new Dictionary<int, Guid> { [1] = suggestion.PassengerId }, ct);
        Assert.True(confirmed.CanCommit);
        Assert.Equal(1, confirmed.MatchedPassengers);
    }

    [Fact]
    public async Task Duplicate_passport_and_conflicting_assignment_block_without_deleting_data()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.CreateAsync(2, 0);
        var existing = new FlightBooking { TripId = fixture.Trip.Id, Pnr = "OLD-FIXTURE", Airline = "Aerolínea ficticia", Status = VerificationStatus.Confirmed };
        existing.PassengerFlights.Add(new PassengerFlight
        {
            PassengerId = fixture.Passengers[0].Id,
            FlightBooking = existing,
            TicketStatus = VerificationStatus.Confirmed,
            ElectronicTicketNumber = "ELECTRONIC-FIXTURE"
        });
        fixture.Db.FlightBookings.Add(existing);
        await fixture.Db.SaveChangesAsync(ct);
        var duplicatePassport = string.Join('\n',
            Header,
            CsvRow(1, "Persona Ficticia 01", "PX001", "FIXTURE1", "CM"),
            CsvRow(2, "Persona Ficticia 02", "PX001", "FIXTURE2", "LA"));

        var preview = await fixture.Service().PreviewAsync(Stream(duplicatePassport), "manifest.csv", true, false, null, ct);

        Assert.False(preview.CanCommit);
        Assert.True(preview.PassportConflicts > 0);
        Assert.Equal(1, preview.ConflictingAssociations);
        Assert.Equal(2, await fixture.Db.Passengers.CountAsync(ct));
        Assert.Equal("ELECTRONIC-FIXTURE", await fixture.Db.PassengerFlights.Select(x => x.ElectronicTicketNumber).SingleAsync(ct));

        var reassignment = SingleRow("Persona Ficticia 01", "NEW-FIXTURE", "CM", "PX009");
        var accepted = await fixture.Service().PreviewAsync(Stream(reassignment), "manifest.csv", true, true, null, ct);
        Assert.True(accepted.CanCommit);
        await fixture.Service().CommitAsync(Stream(reassignment), "manifest.csv", accepted.Sha256, true, true, true,
            null, Guid.NewGuid(), "fixture-admin", ct);
        Assert.Equal(2, await fixture.Db.PassengerFlights.CountAsync(x => x.PassengerId == fixture.Passengers[0].Id, ct));
        Assert.Contains(await fixture.Db.PassengerFlights.Where(x => x.PassengerId == fixture.Passengers[0].Id)
            .Select(x => x.ElectronicTicketNumber).ToListAsync(ct), value => value == "ELECTRONIC-FIXTURE");
    }

    [Fact]
    public async Task Passport_owned_by_another_passenger_and_invalid_temporal_data_are_blocking()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.CreateAsync(2, 0);
        fixture.Passengers[0].PassportNumber = "OWNER-PASSPORT";
        fixture.Passengers[0].NormalizedPassportNumber = TextNormalizer.Normalize("OWNER-PASSPORT");
        await fixture.Db.SaveChangesAsync(ct);
        var csv = string.Join('\n', Header,
            "2;Persona Ficticia 02;OWNER-PASSPORT;2099-01-01;2000-01-01;Pya;FIXTURE2;CM;;");

        var preview = await fixture.Service().PreviewAsync(Stream(csv), "manifest.csv", true, true, null, ct);

        Assert.False(preview.CanCommit);
        Assert.Contains(preview.Issues, x => x.Field == "passport" && x.Level == "Error");
        Assert.Contains(preview.Issues, x => x.Field == "birth_date" && x.Level == "Error");
        Assert.Equal("OWNER-PASSPORT", fixture.Passengers[0].PassportNumber);
        Assert.Null(fixture.Passengers[1].PassportNumber);
    }

    [Fact]
    public async Task Xlsx_headers_are_supported_and_secondary_dates_are_not_persisted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.CreateAsync(1, 1);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Manifest");
        var headers = Header.Split(';');
        for (var index = 0; index < headers.Length; index++) sheet.Cell(1, index + 1).Value = headers[index];
        var values = CsvRow(1, "Persona Ficticia 01", "PX001", "FIXTURE1", "CM").Split(';');
        for (var index = 0; index < values.Length; index++) sheet.Cell(2, index + 1).Value = values[index];
        await using var stream = new MemoryStream();
        workbook.SaveAs(stream); stream.Position = 0;

        var preview = await fixture.Service().PreviewAsync(stream, "manifest.xlsx", true, true, null, ct);

        Assert.True(preview.CanCommit);
        Assert.Equal(1, preview.IgnoredSecondaryDates);
        Assert.Equal(new DateOnly(2026, 8, 1), await fixture.Db.RoomReservations.Select(x => x.CheckIn).SingleAsync(ct));
        Assert.False(await fixture.Db.FlightSegments.AnyAsync(ct));
    }

    private const string Header = "row;name;passport;birth_date;passport_expiry;nationality_code;pnr;airline_code;check_in;check_out";

    private static string BuildManifest(int passengerCount)
    {
        var groups = new (string Pnr, string Airline, int Count)[]
        {
            ("C01","CM",2),("C02","CM",6),("C03","CM",1),("C04","CM",3),("C05","CM",1),("C06","CM",1),
            ("C07","CM",1),("C08","CM",2),("C09","CM",6),("C10","CM",2),("C11","CM",2),("C12","CM",2),
            ("L01","LA",2),("L02","LA",2),("L03","LA",1),("L04","LA",1),("L05","LA",2),("L06","LA",2),
            ("L07","LA",1),("L08","LA",1),("L09","LA",1)
        };
        var assignments = groups.SelectMany(group => Enumerable.Repeat((group.Pnr, group.Airline), group.Count)).ToArray();
        var lines = new List<string> { Header };
        for (var index = 1; index <= passengerCount; index++)
        {
            var assignment = index <= assignments.Length ? assignments[index - 1] : (string.Empty, string.Empty);
            lines.Add(CsvRow(index, $"Persona Ficticia {index:00}", index <= 39 ? $"PX{index:000}" : string.Empty,
                assignment.Item1, assignment.Item2, index <= 40, index <= 39));
        }
        return string.Join('\n', lines);
    }

    private static string SingleRow(string name, string pnr, string airline, string passport) =>
        string.Join('\n', Header, CsvRow(1, name, passport, pnr, airline));

    private static string CsvRow(int row, string name, string passport, string pnr, string airline,
        bool birth = true, bool expiry = true) => string.Join(';',
            row.ToString(CultureInfo.InvariantCulture), name, passport, birth ? "1990-01-01" : string.Empty,
            expiry ? "2030-01-01" : string.Empty, (row % 4) switch { 0 => "Ita", 1 => "Pya", 2 => "Mex", _ => "EEUU" },
            pnr, airline, "2026-09-06", "2026-09-15");

    private static MemoryStream Stream(string value) => new(Encoding.UTF8.GetBytes(value));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AppDbContext Db { get; }
        public Trip Trip { get; }
        public IReadOnlyList<Passenger> Passengers { get; }

        private Fixture(SqliteConnection connection, AppDbContext db, Trip trip, IReadOnlyList<Passenger> passengers)
        { _connection = connection; Db = db; Trip = trip; Passengers = passengers; }

        public PassengerTravelManifestImportService Service() => new(Db,
            NullLogger<PassengerTravelManifestImportService>.Instance);

        public static async Task<Fixture> CreateAsync(int passengerCount, int roomCount, string? firstName = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var trip = new Trip
            {
                Name = $"Viaje ficticio {Guid.NewGuid():N}", Destination = "Destino ficticio",
                StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 15), WeddingDate = new DateOnly(2026, 9, 10)
            };
            var op = new Operator { Name = $"Operadora ficticia {Guid.NewGuid():N}", Type = OperatorType.Agency };
            db.AddRange(trip, op);
            var rooms = Enumerable.Range(1, roomCount).Select(index => new RoomReservation
            {
                Trip = trip, Operator = op, InternalCode = $"ROOM-{index:00}", ExpectedCapacity = 2,
                CheckIn = new DateOnly(2026, 8, 1), CheckOut = new DateOnly(2026, 8, 8)
            }).ToArray();
            db.RoomReservations.AddRange(rooms);
            var passengers = Enumerable.Range(1, passengerCount).Select(index => new Passenger
            {
                Trip = trip,
                FullName = index == 1 && firstName is not null ? firstName : $"Persona Ficticia {index:00}",
                NormalizedName = TextNormalizer.Normalize(index == 1 && firstName is not null ? firstName : $"Persona Ficticia {index:00}"),
                RoomReservation = rooms.Length == 0 ? null : rooms[(index - 1) % rooms.Length],
                DocumentationStatus = VerificationStatus.ToVerify,
                PassportReviewStatus = VerificationStatus.ToVerify
            }).ToArray();
            db.Passengers.AddRange(passengers);
            await db.SaveChangesAsync();
            return new(connection, db, trip, passengers);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
