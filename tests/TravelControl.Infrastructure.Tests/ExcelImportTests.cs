using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;
using TravelControl.Infrastructure.Services;
using Xunit;

namespace TravelControl.Infrastructure.Tests;
public sealed class ExcelImportTests
{
    [Fact]
    public async Task Private_master_has_the_expected_dry_run_counts_when_available()
    {
        var privateDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../data/private"));
        var path = Directory.Exists(privateDirectory) ? Directory.GetFiles(privateDirectory, "*.xlsx").FirstOrDefault() : null;
        if (path is null) return;
        await using var connection = new SqliteConnection("Data Source=:memory:"); var ct = TestContext.Current.CancellationToken;
        await connection.OpenAsync(ct); await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(ct); db.Trips.Add(new Trip { Name = "Viaje de prueba", Destination = "Destino", IsActive = true });
        db.Operators.AddRange(new Operator { Name = "Top Travel", Type = OperatorType.Agency }, new Operator { Name = "Bespoke", Type = OperatorType.HotelOperator }); await db.SaveChangesAsync(ct);
        await using var stream = File.OpenRead(path); var result = await new ExcelImportService(db, NullLogger<ExcelImportService>.Instance).ProcessAsync(stream, "workbook-privado.xlsx", true, null, ct);
        Assert.True(result.CanCommit); Assert.Equal(46, result.PassengerRows); Assert.Equal(25, result.RoomRows);
        Assert.Equal(44, result.ExpectedComparison["topTravelPassengers"]); Assert.Equal(24, result.ExpectedComparison["topTravelRooms"]);
        Assert.Equal(2, result.ExpectedComparison["bespokePassengers"]); Assert.Equal(1, result.ExpectedComparison["bespokeRooms"]);
    }

    [Fact]
    public async Task Fictional_workbook_import_is_idempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        db.Trips.Add(new Trip { Name = "Viaje de prueba", Destination = "Destino", IsActive = true });
        var ct = TestContext.Current.CancellationToken;
        db.Operators.Add(new Operator { Name = "Top Travel", Type = OperatorType.Agency }); await db.SaveChangesAsync(ct);
        var service = new ExcelImportService(db, NullLogger<ExcelImportService>.Instance); var bytes = Workbook();
        var first = await service.ProcessAsync(new MemoryStream(bytes), "anonimizado.xlsx", false, null, ct);
        var second = await service.ProcessAsync(new MemoryStream(bytes), "anonimizado.xlsx", false, null, ct);
        Assert.True(first.CanCommit); Assert.Equal(1, await db.Passengers.CountAsync(ct)); Assert.Equal(1, await db.RoomReservations.CountAsync(ct)); Assert.Equal(0, second.Added);
    }

    private static byte[] Workbook()
    {
        using var workbook = new XLWorkbook(); var rooms = workbook.AddWorksheet("Habitaciones");
        rooms.Cell(1, 1).InsertData(new[] { new[] { "ID habitación", "Operadora", "Estado", "Tipo habitación", "Ocupantes", "Check-in", "Check-out", "Hotel / propiedad", "Fuente", "Observaciones" } });
        rooms.Cell(2, 1).InsertData(new[] { new object[] { "TT-X01", "Top Travel", "Confirmado", "Individual", 1, new DateTime(2026, 9, 6), new DateTime(2026, 9, 11), "Hotel ficticio", "Voucher ficticio", "Caso anonimizado" } });
        var passengers = workbook.AddWorksheet("Control pasajeros");
        passengers.Cell(1, 1).InsertData(new[] { new[] { "Operadora", "Habitación / grupo", "Pasajero", "Estado habitación", "Estado ticket de vuelo", "Maleta 23 kg incluida", "Próxima acción", "Observaciones" } });
        passengers.Cell(2, 1).InsertData(new[] { new[] { "Top Travel", "TT-X01", "Persona Ficticia", "Confirmado", "Por verificar", "Por verificar", "Verificar ticket", "Fixture anonimizado" } });
        using var stream = new MemoryStream(); workbook.SaveAs(stream); return stream.ToArray();
    }
}
