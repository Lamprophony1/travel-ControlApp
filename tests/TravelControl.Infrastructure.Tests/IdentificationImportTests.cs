using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TravelControl.Application.Services;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;
using TravelControl.Infrastructure.Services;
using Xunit;

namespace TravelControl.Infrastructure.Tests;

public sealed class IdentificationImportTests
{
    [Fact]
    public async Task Valid_aliases_and_normalized_name_fill_only_empty_fields_and_are_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var passenger = await fixture.AddPassengerAsync("José   Pérez");
        var bytes = Workbook(
            ["NOMBRE Y APELLIDO", "NRO DE PASP", "FECHA NAC", "NAC", "VENCIMIENTO PASAPORTE"],
            ["  Jose Perez ", "PX-TEST-1234", new DateTime(1990, 2, 3), "Ficticia", new DateTime(2030, 4, 5)]);

        var preview = await fixture.Service.PreviewAsync(new MemoryStream(bytes), "identificacion.xlsx", false, fixture.Ct);
        Assert.True(preview.CanCommit);
        Assert.Equal(1, preview.Matched);
        Assert.Equal(1, preview.WillUpdate);
        Assert.Equal(4, preview.MissingFields);

        var committed = await fixture.Service.CommitAsync(new MemoryStream(bytes), "identificacion.xlsx", false, false,
            fixture.UserId, "admin@example.test", fixture.Ct);
        Assert.NotNull(committed.ImportRunId);
        Assert.Equal(1, await fixture.Db.Passengers.CountAsync(fixture.Ct));
        await fixture.Db.Entry(passenger).ReloadAsync(fixture.Ct);
        Assert.Equal("PX-TEST-1234", passenger.PassportNumber);
        Assert.Equal(new DateOnly(1990, 2, 3), passenger.BirthDate);
        Assert.Equal("Ficticia", passenger.Nationality);
        Assert.Equal(new DateOnly(2030, 4, 5), passenger.PassportExpiry);
        var audit = await fixture.Db.AuditLogs.SingleAsync(fixture.Ct);
        Assert.DoesNotContain("PX-TEST-1234", audit.NewValue);
        Assert.Contains("PassportNumber", audit.NewValue);
        Assert.Contains("234", audit.NewValue);

        var repeated = await fixture.Service.CommitAsync(new MemoryStream(bytes), "identificacion.xlsx", false, false,
            fixture.UserId, "admin@example.test", fixture.Ct);
        Assert.Equal(0, repeated.WillUpdate);
        Assert.Equal(1, repeated.Unchanged);
        Assert.Equal(1, await fixture.Db.Passengers.CountAsync(fixture.Ct));
    }

    [Fact]
    public async Task Existing_values_are_conflicts_until_explicit_overwrite_is_confirmed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var passenger = await fixture.AddPassengerAsync("Persona Ficticia", passport: "OLD-0001", nationality: "Anterior");
        var bytes = Workbook(
            ["PASAJERO", "NÚMERO DE PASAPORTE", "NACIONALIDAD"],
            ["Persona Ficticia", "NEW-0002", "Nueva"]);

        var safePreview = await fixture.Service.PreviewAsync(new MemoryStream(bytes), "conflictos.xlsx", false, fixture.Ct);
        Assert.True(safePreview.CanCommit);
        Assert.Equal(2, safePreview.Conflicts);
        Assert.Equal(0, safePreview.WillOverwrite);
        await fixture.Service.CommitAsync(new MemoryStream(bytes), "conflictos.xlsx", false, false,
            fixture.UserId, null, fixture.Ct);
        await fixture.Db.Entry(passenger).ReloadAsync(fixture.Ct);
        Assert.Equal("OLD-0001", passenger.PassportNumber);
        Assert.Equal("Anterior", passenger.Nationality);

        var overwritePreview = await fixture.Service.PreviewAsync(new MemoryStream(bytes), "conflictos.xlsx", true, fixture.Ct);
        Assert.Equal(2, overwritePreview.WillOverwrite);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CommitAsync(
            new MemoryStream(bytes), "conflictos.xlsx", true, false, fixture.UserId, null, fixture.Ct));
        await fixture.Service.CommitAsync(new MemoryStream(bytes), "conflictos.xlsx", true, true,
            fixture.UserId, null, fixture.Ct);
        await fixture.Db.Entry(passenger).ReloadAsync(fixture.Ct);
        Assert.Equal("NEW-0002", passenger.PassportNumber);
        Assert.Equal("Nueva", passenger.Nationality);
    }

    [Fact]
    public async Task Duplicate_rows_invalid_dates_and_duplicate_passports_block_commit_without_creating_passengers()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddPassengerAsync("Persona Uno");
        await fixture.AddPassengerAsync("Persona Dos");
        var bytes = Workbook(
            ["NOMBRE COMPLETO", "PASAPORTE", "FECHA DE NACIMIENTO"],
            ["Persona Uno", "DUP-9999", "fecha imposible"],
            [" Persona   Uno ", "DUP-9999", "01/01/1990"],
            ["Persona Dos", "DUP-9999", "02/02/1991"],
            ["Sin Coincidencia", "OTHER-1", "03/03/1992"]);

        var result = await fixture.Service.PreviewAsync(new MemoryStream(bytes), "errores.xlsx", false, fixture.Ct);
        Assert.False(result.CanCommit);
        Assert.True(result.Duplicates >= 2);
        Assert.True(result.DuplicatePassports >= 3);
        Assert.True(result.InvalidDates >= 1);
        Assert.Equal(1, result.Unmatched);
        Assert.Equal(2, await fixture.Db.Passengers.CountAsync(fixture.Ct));
    }

    private static byte[] Workbook(string[] headers, params object[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Identificación");
        for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
        for (var row = 0; row < rows.Length; row++)
            for (var column = 0; column < rows[row].Length; column++)
                sheet.Cell(row + 2, column + 1).Value = XLCellValue.FromObject(rows[row][column]);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AppDbContext Db { get; }
        public IdentificationImportService Service { get; }
        public Guid UserId { get; } = Guid.NewGuid();
        public CancellationToken Ct => TestContext.Current.CancellationToken;

        private Fixture(SqliteConnection connection, AppDbContext db)
        {
            _connection = connection;
            Db = db;
            Service = new IdentificationImportService(db, NullLogger<IdentificationImportService>.Instance);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            db.Trips.Add(new Trip { Name = "Viaje ficticio", Destination = "Destino", IsActive = true });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return new Fixture(connection, db);
        }

        public async Task<Passenger> AddPassengerAsync(string name, string? passport = null, string? nationality = null)
        {
            var tripId = await Db.Trips.Select(x => x.Id).SingleAsync(Ct);
            var passenger = new Passenger
            {
                TripId = tripId, FullName = name, NormalizedName = TextNormalizer.Normalize(name),
                PassportNumber = passport, NormalizedPassportNumber = passport is null ? null : TextNormalizer.Normalize(passport),
                Nationality = nationality
            };
            Db.Passengers.Add(passenger);
            await Db.SaveChangesAsync(Ct);
            return passenger;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
