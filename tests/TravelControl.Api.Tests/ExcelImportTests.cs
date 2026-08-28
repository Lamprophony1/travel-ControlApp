using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TravelControl.Api.Data;
using TravelControl.Api.Domain;
using TravelControl.Api.Services;
using Xunit;

namespace TravelControl.Api.Tests;

public sealed class ExcelImportTests
{
    [Fact]
    public async Task Fictional_workbook_import_is_idempotent()
    {
        await using var db = Database(); await Seed(db);
        var service = new ExcelImportService(db, NullLogger<ExcelImportService>.Instance);
        var bytes = FictionalWorkbook();
        var ct = TestContext.Current.CancellationToken;
        var first = await service.ProcessAsync(new MemoryStream(bytes), "anonimizado.xlsx", false, null, ct);
        var second = await service.ProcessAsync(new MemoryStream(bytes), "anonimizado.xlsx", false, null, ct);
        Assert.True(first.CanCommit); Assert.Equal(1, await db.Passengers.CountAsync(ct)); Assert.Equal(1, await db.RoomReservations.CountAsync(ct));
        Assert.Equal(0, second.Added); Assert.Equal(2, second.Unchanged);
    }

    [Fact]
    public async Task Private_master_workbook_produces_expected_preview_when_available()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../data/private/Control_viaje_boda_Cielito_Ronaldo.xlsx"));
        if (!File.Exists(path)) return;
        await using var db = Database(); await Seed(db);
        var service = new ExcelImportService(db, NullLogger<ExcelImportService>.Instance);
        await using var stream = File.OpenRead(path);
        var result = await service.ProcessAsync(stream, Path.GetFileName(path), true, null, TestContext.Current.CancellationToken);
        Assert.Equal(46, result.PassengerRows); Assert.Equal(25, result.RoomRows); Assert.True(result.CanCommit);
        Assert.Equal(44, result.ExpectedComparison["topTravelPassengers"]); Assert.Equal(2, result.ExpectedComparison["bespokePassengers"]);
    }

    private static AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task Seed(AppDbContext db)
    {
        db.Trips.Add(new Trip { Name = "Boda Cielito & Ronaldo", Destination = "Riviera Maya", StartDate = new DateOnly(2026, 9, 6), EndDate = new DateOnly(2026, 9, 15), WeddingDate = new DateOnly(2026, 9, 9) });
        db.Operators.Add(new Operator { Name = "Top Travel", Type = OperatorType.Agency }); await db.SaveChangesAsync();
    }

    private static byte[] FictionalWorkbook()
    {
        using var workbook = new XLWorkbook();
        var rooms = workbook.AddWorksheet("Habitaciones");
        rooms.Cell(1, 1).InsertData(new[] { new[] { "ID habitación", "Operadora", "Estado", "Tipo habitación", "Ocupantes", "Check-in", "Check-out", "Hotel / propiedad", "Fuente", "Observaciones" } });
        rooms.Cell(2, 1).InsertData(new[] { new object[] { "TT-X01", "Top Travel", "Confirmado", "Individual", 1, new DateTime(2026, 9, 6), new DateTime(2026, 9, 11), "Hotel ficticio", "Voucher ficticio", "Caso anonimizado" } });
        var passengers = workbook.AddWorksheet("Control pasajeros");
        passengers.Cell(1, 1).InsertData(new[] { new[] { "Operadora", "Habitación / grupo", "Pasajero", "Estado habitación", "Estado ticket de vuelo", "Maleta 23 kg incluida", "Estado transfer", "Próxima acción", "Responsable", "Observaciones" } });
        passengers.Cell(2, 1).InsertData(new[] { new[] { "Top Travel", "TT-X01", "Persona Ficticia", "Confirmado", "Por verificar", "Por verificar", "Por verificar", "Verificar ticket", "QA", "Fixture anonimizado" } });
        using var stream = new MemoryStream(); workbook.SaveAs(stream); return stream.ToArray();
    }
}
