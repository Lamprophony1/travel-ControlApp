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

    [Fact]
    public async Task Named_sheet_priority_scoring_ties_and_explicit_selection_are_deterministic()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddPassengerAsync("Persona Uno");
        using var workbook = new XLWorkbook();
        AddSheet(workbook, "Resumen", ["Nombre"], ["Persona Uno"]);
        AddSheet(workbook, "Pasaportes", ["Nombre", "Pasaporte"], ["Persona Uno", "PASS-1"]);
        AddSheet(workbook, "Identificación", ["Nombre", "Pasaporte", "Nacionalidad"], ["Persona Uno", "PASS-2", "Ficticia"]);
        var prioritized = await fixture.Service.PreviewAsync(new MemoryStream(Bytes(workbook)), "sheets.xlsx", false, fixture.Ct);
        Assert.Equal("Identificación", prioritized.SelectedSheet);

        using var namedWorkbook = new XLWorkbook();
        AddSheet(namedWorkbook, "Documentos", ["Nombre", "Pasaporte", "Nacionalidad"], ["Persona Uno", "PASS-D", "Ficticia"]);
        AddSheet(namedWorkbook, "Pasaportes", ["Nombre", "Pasaporte"], ["Persona Uno", "PASS-P"]);
        var named = await fixture.Service.PreviewAsync(new MemoryStream(Bytes(namedWorkbook)), "named.xlsx", false, fixture.Ct);
        Assert.Equal("Pasaportes", named.SelectedSheet);

        using var scoredWorkbook = new XLWorkbook();
        AddSheet(scoredWorkbook, "Datos mínimos", ["Nombre", "Pasaporte"], ["Persona Uno", "PASS-M"]);
        AddSheet(scoredWorkbook, "Ficha completa", ["Nombre", "Pasaporte", "Fecha de nacimiento", "Nacionalidad", "Vencimiento"],
            ["Persona Uno", "PASS-C", new DateTime(1990, 1, 1), "Ficticia", new DateTime(2030, 1, 1)]);
        var scored = await fixture.Service.PreviewAsync(new MemoryStream(Bytes(scoredWorkbook)), "scored.xlsx", false, fixture.Ct);
        Assert.Equal("Ficha completa", scored.SelectedSheet);

        using var tiedWorkbook = new XLWorkbook();
        AddSheet(tiedWorkbook, "Hoja A", ["Nombre", "Pasaporte"], ["Persona Uno", "PASS-A"]);
        AddSheet(tiedWorkbook, "Hoja B", ["Nombre", "Pasaporte"], ["Persona Uno", "PASS-B"]);
        var tiedBytes = Bytes(tiedWorkbook);
        var tied = await fixture.Service.PreviewAsync(new MemoryStream(tiedBytes), "tie.xlsx", false, fixture.Ct);
        Assert.False(tied.CanCommit);
        Assert.Equal(["Hoja A", "Hoja B"], tied.CandidateSheets);
        var explicitSheet = await fixture.Service.PreviewAsync(new MemoryStream(tiedBytes), "tie.xlsx", false, fixture.Ct, "Hoja B");
        Assert.True(explicitSheet.CanCommit);
        Assert.Equal("Hoja B", explicitSheet.SelectedSheet);
    }

    [Fact]
    public async Task Temporal_inconsistencies_block_and_expired_passports_remain_warnings()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddPassengerAsync("Fecha Futura");
        await fixture.AddPassengerAsync("Edad Imposible");
        await fixture.AddPassengerAsync("Vencimiento Antiguo");
        var invalid = Workbook(["Nombre", "Fecha de nacimiento", "Vencimiento"],
            ["Fecha Futura", new DateTime(2027, 1, 1), new DateTime(2030, 1, 1)],
            ["Edad Imposible", new DateTime(1800, 1, 1), new DateTime(2030, 1, 1)],
            ["Vencimiento Antiguo", new DateTime(2000, 1, 1), new DateTime(1999, 1, 1)]);
        var blocked = await fixture.Service.PreviewAsync(new MemoryStream(invalid), "temporal.xlsx", false, fixture.Ct);
        Assert.False(blocked.CanCommit);
        Assert.Equal(3, blocked.TemporallyInconsistentRows);

        var expired = Workbook(["Nombre", "Fecha de nacimiento", "Vencimiento"],
            ["Vencimiento Antiguo", new DateTime(2000, 1, 1), new DateTime(2025, 1, 1)]);
        var warning = await fixture.Service.PreviewAsync(new MemoryStream(expired), "expired.xlsx", false, fixture.Ct);
        Assert.True(warning.CanCommit);
        Assert.Equal(1, warning.ExpiredPassports);
        Assert.Contains(warning.Issues, x => x.Level == "Advertencia" && x.Message.Contains("preventivo", StringComparison.OrdinalIgnoreCase));

        await fixture.AddPassengerAsync("Vence antes del regreso");
        await fixture.AddPassengerAsync("Vence en umbral");
        var preventive = Workbook(["Nombre", "Fecha de nacimiento", "Vencimiento"],
            ["Vence antes del regreso", new DateTime(1990, 1, 1), new DateTime(2026, 9, 10)],
            ["Vence en umbral", new DateTime(1990, 1, 1), new DateTime(2027, 1, 1)]);
        var preventiveWarnings = await fixture.Service.PreviewAsync(new MemoryStream(preventive), "preventive.xlsx", false, fixture.Ct);
        Assert.True(preventiveWarnings.CanCommit);
        Assert.Equal(1, preventiveWarnings.ExpiriesBeforeReturn);
        Assert.Equal(1, preventiveWarnings.ExpiriesWithinWarningThreshold);
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

    private static void AddSheet(XLWorkbook workbook, string name, string[] headers, params object[][] rows)
    {
        var sheet = workbook.AddWorksheet(name);
        for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
        for (var row = 0; row < rows.Length; row++)
            for (var column = 0; column < rows[row].Length; column++)
                sheet.Cell(row + 2, column + 1).Value = XLCellValue.FromObject(rows[row][column]);
    }
    private static byte[] Bytes(XLWorkbook workbook)
    {
        using var stream = new MemoryStream(); workbook.SaveAs(stream); return stream.ToArray();
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
            db.Trips.Add(new Trip { Name = "Viaje ficticio", Destination = "Destino", IsActive = true,
                EndDate = new DateOnly(2026, 9, 15), PassportWarningDays = 180 });
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
