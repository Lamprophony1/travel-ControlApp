using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TravelControl.Api.Data;
using TravelControl.Api.Domain;

namespace TravelControl.Api.Services;

public sealed record ImportIssue(string Level, string Sheet, int? Row, string Message);
public sealed record ImportSummary(
    int PassengerRows, int RoomRows, int Added, int Updated, int Unchanged, int Errors,
    bool CanCommit, IReadOnlyList<ImportIssue> Issues, IReadOnlyDictionary<string, int> ExpectedComparison,
    Guid? ImportRunId = null);

internal sealed record RoomRow(string Code, string Operator, VerificationStatus Status, string? Type,
    int Capacity, DateOnly? CheckIn, DateOnly? CheckOut, string? Hotel, string? Source, string? Notes, int Row);
internal sealed record PassengerRow(string Name, string Operator, string? RoomCode, VerificationStatus RoomStatus,
    VerificationStatus TicketStatus, VerificationStatus BaggageStatus, VerificationStatus TransferStatus,
    string? NextAction, string? Owner, string? Notes, int Row);

public sealed class ExcelImportService(AppDbContext db, ILogger<ExcelImportService> logger)
{
    private static readonly string[] RafaClara =
    [
        "RAFAEL NICOLAS GARCIA BRITOS",
        "CLARA MARIA DE LOS ANGELES ROLON DURE"
    ];

    public async Task<ImportSummary> ProcessAsync(Stream stream, string fileName, bool dryRun, Guid? userId, CancellationToken ct)
    {
        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var issues = new List<ImportIssue>();

        var roomSheet = FindSheet(workbook, "Habitaciones");
        var passengerSheet = FindSheet(workbook, "Control pasajeros");
        if (roomSheet is null) issues.Add(new("Error", "Habitaciones", null, "No se encontró la hoja autoritativa Habitaciones."));
        if (passengerSheet is null) issues.Add(new("Error", "Control pasajeros", null, "No se encontró la hoja autoritativa Control pasajeros."));
        if (issues.Count > 0) return new(0, 0, 0, 0, 0, issues.Count, false, issues, new Dictionary<string, int>());

        var rooms = ParseRooms(roomSheet!, issues);
        var passengers = ParsePassengers(passengerSheet!, issues);
        passengers = passengers
            .GroupBy(x => TextNormalizer.Normalize(x.Name))
            .Select(g => g.OrderByDescending(x => TextNormalizer.Normalize(x.Operator) == "BESPOKE").First())
            .ToList();

        foreach (var person in passengers.Where(x => RafaClara.Contains(TextNormalizer.Normalize(x.Name))
                                                      && TextNormalizer.Normalize(x.Operator) != "BESPOKE"))
            issues.Add(new("Error", "Control pasajeros", person.Row, $"{person.Name} debe pertenecer únicamente a Bespoke."));

        var comparison = new Dictionary<string, int>
        {
            ["passengers"] = passengers.Count,
            ["rooms"] = rooms.Count,
            ["topTravelPassengers"] = passengers.Count(x => TextNormalizer.Normalize(x.Operator) == "TOP TRAVEL"),
            ["topTravelRooms"] = rooms.Count(x => TextNormalizer.Normalize(x.Operator) == "TOP TRAVEL"),
            ["bespokePassengers"] = passengers.Count(x => TextNormalizer.Normalize(x.Operator) == "BESPOKE"),
            ["bespokeRooms"] = rooms.Count(x => TextNormalizer.Normalize(x.Operator) == "BESPOKE")
        };
        WarnCount(comparison, "passengers", 46, "pasajeros", issues);
        WarnCount(comparison, "rooms", 25, "habitaciones", issues);
        WarnCount(comparison, "topTravelPassengers", 44, "pasajeros Top Travel", issues);
        WarnCount(comparison, "topTravelRooms", 24, "habitaciones Top Travel", issues);
        WarnCount(comparison, "bespokePassengers", 2, "pasajeros Bespoke", issues);
        WarnCount(comparison, "bespokeRooms", 1, "habitaciones Bespoke", issues);

        var trip = await db.Trips.SingleAsync(x => x.IsActive, ct);
        var existingRooms = await db.RoomReservations.AsNoTracking().Where(x => x.TripId == trip.Id).ToDictionaryAsync(x => TextNormalizer.Normalize(x.InternalCode), ct);
        var existingPassengers = await db.Passengers.AsNoTracking().Where(x => x.TripId == trip.Id).ToDictionaryAsync(x => x.NormalizedName, ct);
        var added = 0; var updated = 0; var unchanged = 0;

        foreach (var row in rooms)
        {
            if (!existingRooms.TryGetValue(TextNormalizer.Normalize(row.Code), out var old)) added++;
            else if (RoomChanged(old, row)) updated++; else unchanged++;
        }
        foreach (var row in passengers)
        {
            if (!existingPassengers.TryGetValue(TextNormalizer.Normalize(row.Name), out var old)) added++;
            else if (PassengerChanged(old, row)) updated++; else unchanged++;
        }

        var errorCount = issues.Count(x => x.Level == "Error");
        var canCommit = errorCount == 0;
        if (dryRun || !canCommit)
            return new(passengers.Count, rooms.Count, added, updated, unchanged, errorCount, canCommit, issues, comparison);

        var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var operators = await db.Operators.ToDictionaryAsync(x => TextNormalizer.Normalize(x.Name), ct);
            foreach (var name in rooms.Select(x => x.Operator).Concat(passengers.Select(x => x.Operator)).DistinctBy(TextNormalizer.Normalize))
            {
                var key = TextNormalizer.Normalize(name);
                if (operators.ContainsKey(key)) continue;
                var op = new Operator { Name = name.Trim(), Type = OperatorType.Agency };
                db.Operators.Add(op); operators[key] = op;
            }
            await db.SaveChangesAsync(ct);

            var roomEntities = await db.RoomReservations.Include(x => x.Passengers).Where(x => x.TripId == trip.Id)
                .ToDictionaryAsync(x => TextNormalizer.Normalize(x.InternalCode), ct);
            foreach (var row in rooms)
            {
                var key = TextNormalizer.Normalize(row.Code);
                if (!roomEntities.TryGetValue(key, out var entity))
                {
                    entity = new RoomReservation { TripId = trip.Id, InternalCode = row.Code.Trim(), OperatorId = operators[TextNormalizer.Normalize(row.Operator)].Id };
                    db.RoomReservations.Add(entity); roomEntities[key] = entity;
                }
                ApplyRoom(entity, row, operators[TextNormalizer.Normalize(row.Operator)]);
            }
            await db.SaveChangesAsync(ct);

            var passengerEntities = await db.Passengers.Include(x => x.BaggageEntitlements)
                .Where(x => x.TripId == trip.Id).ToDictionaryAsync(x => x.NormalizedName, ct);
            foreach (var row in passengers)
            {
                var key = TextNormalizer.Normalize(row.Name);
                if (!passengerEntities.TryGetValue(key, out var entity))
                {
                    entity = new Passenger { TripId = trip.Id, Trip = trip, FullName = row.Name.Trim(), NormalizedName = key, CreatedById = userId };
                    db.Passengers.Add(entity); passengerEntities[key] = entity;
                }
                entity.FullName = row.Name.Trim();
                entity.PrimaryOperatorId = operators[TextNormalizer.Normalize(row.Operator)].Id;
                entity.RoomReservationId = string.IsNullOrWhiteSpace(row.RoomCode) ? null : roomEntities.GetValueOrDefault(TextNormalizer.Normalize(row.RoomCode))?.Id;
                entity.NextAction = row.NextAction;
                entity.InternalOwner = row.Owner;
                entity.Notes = row.Notes;
                entity.UpdatedById = userId;

                if (!entity.BaggageEntitlements.Any())
                    entity.BaggageEntitlements.Add(new BaggageEntitlement { Status = row.BaggageStatus });
            }
            await db.SaveChangesAsync(ct);

            var run = new ImportRun
            {
                FileName = Path.GetFileName(fileName), Sha256 = hash, DryRun = false, Status = "Completado",
                Added = added, Updated = updated, Unchanged = unchanged, Errors = errorCount,
                SummaryJson = JsonSerializer.Serialize(comparison), UserId = userId
            };
            db.ImportRuns.Add(run);
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            logger.LogInformation("Importación {ImportId} completada: {Added} altas, {Updated} actualizaciones", run.Id, added, updated);
            return new(passengers.Count, rooms.Count, added, updated, unchanged, errorCount, true, issues, comparison, run.Id);
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    private static IXLWorksheet? FindSheet(XLWorkbook wb, string expected) =>
        wb.Worksheets.FirstOrDefault(x => TextNormalizer.Normalize(x.Name) == TextNormalizer.Normalize(expected));

    private static List<RoomRow> ParseRooms(IXLWorksheet sheet, List<ImportIssue> issues)
    {
        var headerRow = FindHeaderRow(sheet, "ID HABITACION", "OPERADORA", "CHECK-IN", "CHECK-OUT");
        if (headerRow is null) { issues.Add(new("Error", sheet.Name, null, "No se encontró el encabezado ID habitación.")); return []; }
        var h = Headers(sheet, headerRow.Value);
        var result = new List<RoomRow>();
        foreach (var row in sheet.RowsUsed().Where(x => x.RowNumber() > headerRow))
        {
            var code = Value(row, h, "ID HABITACION");
            if (string.IsNullOrWhiteSpace(code)) continue;
            var checkIn = Date(row, h, "CHECK-IN"); var checkOut = Date(row, h, "CHECK-OUT");
            if (checkIn >= checkOut) issues.Add(new("Error", sheet.Name, row.RowNumber(), "El check-out debe ser posterior al check-in."));
            result.Add(new(code, Value(row, h, "OPERADORA") ?? "", ParseStatus(Value(row, h, "ESTADO")),
                Value(row, h, "TIPO HABITACION"), Int(row, h, "OCUPANTES"), checkIn, checkOut,
                Value(row, h, "HOTEL / PROPIEDAD"), Value(row, h, "FUENTE"), Value(row, h, "OBSERVACIONES"), row.RowNumber()));
        }
        return result.GroupBy(x => TextNormalizer.Normalize(x.Code)).Select(x => x.First()).ToList();
    }

    private static List<PassengerRow> ParsePassengers(IXLWorksheet sheet, List<ImportIssue> issues)
    {
        var headerRow = FindHeaderRow(sheet, "PASAJERO", "OPERADORA", "ESTADO HABITACION", "ESTADO TICKET DE VUELO");
        if (headerRow is null) { issues.Add(new("Error", sheet.Name, null, "No se encontró el encabezado Pasajero.")); return []; }
        var h = Headers(sheet, headerRow.Value);
        var result = new List<PassengerRow>();
        foreach (var row in sheet.RowsUsed().Where(x => x.RowNumber() > headerRow))
        {
            var name = Value(row, h, "PASAJERO");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var ticket = ParseStatus(Value(row, h, "ESTADO TICKET DE VUELO"));
            if (ticket == VerificationStatus.Confirmed)
            {
                ticket = VerificationStatus.ToVerify;
                issues.Add(new("Advertencia", sheet.Name, row.RowNumber(), "El ticket figuraba confirmado sin datos verificables; se importará Por verificar."));
            }
            result.Add(new(name, Value(row, h, "OPERADORA") ?? "", Value(row, h, "HABITACION / GRUPO"),
                ParseStatus(Value(row, h, "ESTADO HABITACION")), ticket,
                ParseStatus(Value(row, h, "MALETA 23 KG INCLUIDA")), ParseStatus(Value(row, h, "ESTADO TRANSFER")),
                Value(row, h, "PROXIMA ACCION"), Value(row, h, "RESPONSABLE"), Value(row, h, "OBSERVACIONES"), row.RowNumber()));
        }
        return result;
    }

    private static int? FindHeaderRow(IXLWorksheet sheet, params string[] required)
    {
        foreach (var row in sheet.RowsUsed())
        {
            var cells = row.CellsUsed().Select(c => TextNormalizer.Normalize(c.GetFormattedString())).ToHashSet();
            if (required.All(cells.Contains)) return row.RowNumber();
        }
        return null;
    }

    private static Dictionary<string, int> Headers(IXLWorksheet sheet, int row) => sheet.Row(row).CellsUsed()
        .ToDictionary(x => TextNormalizer.Normalize(x.GetFormattedString()), x => x.Address.ColumnNumber);
    private static string? Value(IXLRow row, Dictionary<string, int> h, string key) =>
        h.TryGetValue(key, out var col) ? NullIfBlank(row.Cell(col).GetFormattedString()) : null;
    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static int Int(IXLRow row, Dictionary<string, int> h, string key) =>
        h.TryGetValue(key, out var col) && row.Cell(col).TryGetValue<int>(out var value) ? value : 0;
    private static DateOnly? Date(IXLRow row, Dictionary<string, int> h, string key)
    {
        if (!h.TryGetValue(key, out var col)) return null;
        var cell = row.Cell(col);
        if (cell.TryGetValue<DateTime>(out var value)) return DateOnly.FromDateTime(value);
        return DateOnly.TryParse(cell.GetFormattedString(), out var parsed) ? parsed : null;
    }

    private static VerificationStatus ParseStatus(string? text) => TextNormalizer.Normalize(text) switch
    {
        "CONFIRMADO" => VerificationStatus.Confirmed,
        "EN GESTION" => VerificationStatus.InProgress,
        "NO INCLUIDO" => VerificationStatus.NotIncluded,
        "NO APLICA" => VerificationStatus.NotApplicable,
        _ => VerificationStatus.ToVerify
    };

    private static void WarnCount(Dictionary<string, int> values, string key, int expected, string label, List<ImportIssue> issues)
    {
        if (values[key] != expected) issues.Add(new("Advertencia", "Validación", null,
            $"Se esperaban aproximadamente {expected} {label}; el archivo contiene {values[key]}. Revisión administrativa requerida."));
    }

    private static bool RoomChanged(RoomReservation old, RoomRow row) =>
        old.InternalCode != row.Code.Trim() || old.Status != row.Status || old.RoomType != row.Type || old.ExpectedCapacity != row.Capacity
        || old.CheckIn != row.CheckIn || old.CheckOut != row.CheckOut || old.Hotel != row.Hotel || old.SourceReference != row.Source || old.Notes != row.Notes;
    private static bool PassengerChanged(Passenger old, PassengerRow row) =>
        old.FullName != row.Name.Trim() || old.NextAction != row.NextAction || old.InternalOwner != row.Owner || old.Notes != row.Notes;

    private static void ApplyRoom(RoomReservation entity, RoomRow row, Operator op)
    {
        entity.InternalCode = row.Code.Trim(); entity.OperatorId = op.Id; entity.Status = row.Status;
        entity.RoomType = row.Type; entity.ExpectedCapacity = row.Capacity; entity.CheckIn = row.CheckIn; entity.CheckOut = row.CheckOut;
        entity.Hotel = row.Hotel; entity.SourceReference = row.Source; entity.Notes = row.Notes;
        entity.SpecificPropertyPending = TextNormalizer.Normalize(op.Name) == "TOP TRAVEL"
            && (TextNormalizer.Normalize(row.Hotel).Contains("PROPIEDAD EXACTA") || TextNormalizer.Normalize(row.Notes).Contains("PROPIEDAD EXACTA"));
        if (TextNormalizer.Normalize(op.Name) == "BESPOKE")
        {
            entity.Hotel = "Grand Palladium Select White Sand Resort & Spa";
            entity.RoomType = "Junior Suite Garden View"; entity.MealPlan = "All Inclusive";
            entity.CheckIn = new DateOnly(2026, 9, 6); entity.CheckOut = new DateOnly(2026, 9, 11);
            entity.ExpectedCapacity = 2; entity.Status = VerificationStatus.Confirmed; entity.OperatorContact = "595 21 608-508";
            entity.SpecificPropertyPending = false;
        }
    }
}
