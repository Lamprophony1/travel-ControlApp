using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.Json;
using TravelControl.Application.Services;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;

namespace TravelControl.Infrastructure.Services;

public sealed record ImportIssue(string Level, string Sheet, int? Row, string Message);
public sealed record ImportSummary(int PassengerRows, int RoomRows, int Added, int Updated, int Unchanged, int Errors,
    bool CanCommit, IReadOnlyList<ImportIssue> Issues, IReadOnlyDictionary<string, int> ExpectedComparison, Guid? ImportRunId = null);
public enum EnrichmentKind { GuestList, Identification }
public sealed record EnrichmentSummary(int Matched, int Updated, int Unmatched, IReadOnlyList<ImportIssue> Issues);

internal sealed record RoomRow(string Code, string Operator, VerificationStatus Status, string? Type, int Capacity,
    DateOnly? CheckIn, DateOnly? CheckOut, string? Hotel, string? ReservationNumber, string? MealPlan,
    string? Source, string? Contact, string? Notes, int Row);
internal sealed record PassengerRow(string Name, string Operator, string? RoomCode, VerificationStatus DocumentationStatus,
    string? NextAction, string? Notes, int Row);

public sealed class ExcelImportService(AppDbContext db, ILogger<ExcelImportService> logger)
{
    private static readonly string[] LegacyHeaders = ["ESTADO TRANSFER", "COBERTURA TRANSFER", "EMPRESA TRANSFER", "VOUCHER TRANSFER", "RESPONSABLE"];

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
        if (issues.Count > 0) return EmptyFailure(issues);

        var rooms = ParseRooms(roomSheet!, issues);
        var passengers = ResolvePassengerDuplicates(ParsePassengers(passengerSheet!, issues), issues);
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
        WarnCount(comparison, "bespokeRooms", 1, "habitación Bespoke", issues);

        var trip = await db.Trips.SingleAsync(x => x.IsActive, ct);
        var existingRooms = await db.RoomReservations.AsNoTracking().Where(x => x.TripId == trip.Id)
            .ToDictionaryAsync(x => TextNormalizer.Normalize(x.InternalCode), ct);
        var existingPassengers = await db.Passengers.AsNoTracking().Where(x => x.TripId == trip.Id)
            .ToDictionaryAsync(x => x.NormalizedName, ct);
        var added = 0; var updated = 0; var unchanged = 0;
        foreach (var row in rooms)
            if (!existingRooms.TryGetValue(TextNormalizer.Normalize(row.Code), out var old)) added++;
            else if (RoomChanged(old, row)) updated++; else unchanged++;
        foreach (var row in passengers)
            if (!existingPassengers.TryGetValue(TextNormalizer.Normalize(row.Name), out var old)) added++;
            else if (PassengerChanged(old, row)) updated++; else unchanged++;

        var errorCount = issues.Count(x => x.Level == "Error");
        if (dryRun || errorCount > 0)
            return new(passengers.Count, rooms.Count, added, updated, unchanged, errorCount, errorCount == 0, issues, comparison);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var operators = await db.Operators.ToDictionaryAsync(x => TextNormalizer.Normalize(x.Name), ct);
            foreach (var name in rooms.Select(x => x.Operator).Concat(passengers.Select(x => x.Operator)).Where(x => !string.IsNullOrWhiteSpace(x)).DistinctBy(TextNormalizer.Normalize))
            {
                var key = TextNormalizer.Normalize(name);
                if (operators.ContainsKey(key)) continue;
                var entity = new Operator { Name = name.Trim(), Type = OperatorType.Agency };
                db.Operators.Add(entity); operators[key] = entity;
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

            var passengerEntities = await db.Passengers.Where(x => x.TripId == trip.Id).ToDictionaryAsync(x => x.NormalizedName, ct);
            foreach (var row in passengers)
            {
                var key = TextNormalizer.Normalize(row.Name);
                if (!passengerEntities.TryGetValue(key, out var entity))
                {
                    entity = new Passenger { TripId = trip.Id, Trip = trip, FullName = row.Name.Trim(), NormalizedName = key, CreatedById = userId };
                    db.Passengers.Add(entity); passengerEntities[key] = entity;
                }
                entity.FullName = row.Name.Trim();
                entity.PrimaryOperatorId = operators.GetValueOrDefault(TextNormalizer.Normalize(row.Operator))?.Id;
                entity.RoomReservationId = string.IsNullOrWhiteSpace(row.RoomCode) ? null : roomEntities.GetValueOrDefault(TextNormalizer.Normalize(row.RoomCode))?.Id;
                entity.DocumentationStatus = row.DocumentationStatus;
                entity.NextAction = row.NextAction;
                entity.Notes = row.Notes;
                entity.UpdatedById = userId;
            }
            await db.SaveChangesAsync(ct);

            var run = new ImportRun { FileName = Path.GetFileName(fileName), Sha256 = hash, DryRun = false, Status = "Completado",
                Added = added, Updated = updated, Unchanged = unchanged, Errors = errorCount,
                SummaryJson = JsonSerializer.Serialize(comparison), UserId = userId };
            db.ImportRuns.Add(run);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            logger.LogInformation("Import {ImportId} completed. Hash={Hash}; passengers={Passengers}; rooms={Rooms}; added={Added}; updated={Updated}",
                run.Id, hash, passengers.Count, rooms.Count, added, updated);
            return new(passengers.Count, rooms.Count, added, updated, unchanged, 0, true, issues, comparison, run.Id);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            logger.LogError("Import failed. Hash={Hash}; passengers={Passengers}; rooms={Rooms}", hash, passengers.Count, rooms.Count);
            throw;
        }
    }

    public async Task<EnrichmentSummary> EnrichAsync(Stream stream, EnrichmentKind kind, Guid? userId, CancellationToken ct)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = kind == EnrichmentKind.GuestList
            ? FindSheet(workbook, "GUEST LIST") ?? workbook.Worksheets.First()
            : workbook.Worksheets.First();
        var header = FindFlexibleHeader(sheet, ["PASAJERO", "NOMBRE", "NOMBRE COMPLETO"]);
        var issues = new List<ImportIssue>();
        if (header is null) return new(0, 0, 0, [new("Error", sheet.Name, null, "No se encontró una columna de nombre para enriquecer.")]);
        var headers = Headers(sheet, header.Value);
        var passengers = await db.Passengers.ToDictionaryAsync(x => x.NormalizedName, ct);
        var matched = 0; var updated = 0; var unmatched = 0;
        foreach (var row in sheet.RowsUsed().Where(x => x.RowNumber() > header.Value))
        {
            var name = ValueAny(row, headers, "PASAJERO", "NOMBRE COMPLETO", "NOMBRE");
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!passengers.TryGetValue(TextNormalizer.Normalize(name), out var passenger))
            {
                unmatched++; issues.Add(new("Advertencia", sheet.Name, row.RowNumber(), "Fila sin coincidencia en el control maestro.")); continue;
            }
            matched++;
            if (kind == EnrichmentKind.GuestList)
            {
                passenger.EstimatedHotelArrival = ValueAny(row, headers, "HORA ESTIMADA DE LLEGADA", "LLEGADA", "ETA") ?? passenger.EstimatedHotelArrival;
                passenger.DietaryRestrictions = ValueAny(row, headers, "ALERGIAS", "RESTRICCIONES", "ALERGIAS / RESTRICCIONES") ?? passenger.DietaryRestrictions;
                passenger.Notes = MergeNotes(passenger.Notes, ValueAny(row, headers, "NOTAS", "OBSERVACIONES"));
            }
            else
            {
                passenger.BirthDate = DateAny(row, headers, "FECHA DE NACIMIENTO", "NACIMIENTO") ?? passenger.BirthDate;
                passenger.Nationality = ValueAny(row, headers, "NACIONALIDAD") ?? passenger.Nationality;
                passenger.PassportNumber = ValueAny(row, headers, "PASAPORTE", "NUMERO DE PASAPORTE") ?? passenger.PassportNumber;
                passenger.NormalizedPassportNumber = string.IsNullOrWhiteSpace(passenger.PassportNumber) ? null : TextNormalizer.Normalize(passenger.PassportNumber);
                passenger.PassportExpiry = DateAny(row, headers, "VENCIMIENTO", "VENCIMIENTO PASAPORTE") ?? passenger.PassportExpiry;
            }
            passenger.UpdatedById = userId; updated++;
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Structured enrichment completed. kind={Kind}; matched={Matched}; updated={Updated}; unmatched={Unmatched}", kind, matched, updated, unmatched);
        return new(matched, updated, unmatched, issues);
    }

    private static ImportSummary EmptyFailure(List<ImportIssue> issues) => new(0, 0, 0, 0, 0, issues.Count, false, issues, new Dictionary<string, int>());
    private static IXLWorksheet? FindSheet(XLWorkbook workbook, string expected) => workbook.Worksheets.FirstOrDefault(x => TextNormalizer.Normalize(x.Name) == TextNormalizer.Normalize(expected));
    private static List<PassengerRow> ResolvePassengerDuplicates(List<PassengerRow> values, List<ImportIssue> issues) => values
        .GroupBy(x => TextNormalizer.Normalize(x.Name))
        .Select(group =>
        {
            var selected = group.OrderByDescending(x => x.Row).First();
            if (group.Select(x => TextNormalizer.Normalize(x.Operator)).Distinct().Count() > 1)
                issues.Add(new("Advertencia", "Control pasajeros", selected.Row, "Conflicto de operadora resuelto usando la última fila autoritativa del control maestro."));
            return selected;
        }).ToList();

    private static List<RoomRow> ParseRooms(IXLWorksheet sheet, List<ImportIssue> issues)
    {
        var header = FindHeaderRow(sheet, "ID HABITACION", "OPERADORA", "CHECK IN", "CHECK OUT");
        if (header is null) { issues.Add(new("Error", sheet.Name, null, "No se encontró el encabezado ID habitación.")); return []; }
        var headers = Headers(sheet, header.Value); WarnLegacyHeaders(headers, sheet.Name, issues);
        var result = new List<RoomRow>();
        foreach (var row in sheet.RowsUsed().Where(x => x.RowNumber() > header.Value))
        {
            var code = ValueAny(row, headers, "ID HABITACION", "HABITACION / GRUPO");
            if (string.IsNullOrWhiteSpace(code)) continue;
            var checkIn = DateAny(row, headers, "CHECK IN"); var checkOut = DateAny(row, headers, "CHECK OUT");
            if (checkIn >= checkOut) issues.Add(new("Error", sheet.Name, row.RowNumber(), "El check-out debe ser posterior al check-in."));
            result.Add(new(code, ValueAny(row, headers, "OPERADORA") ?? "", ParseStatus(ValueAny(row, headers, "ESTADO")),
                ValueAny(row, headers, "TIPO HABITACION"), IntAny(row, headers, "OCUPANTES", "CAPACIDAD"), checkIn, checkOut,
                ValueAny(row, headers, "HOTEL / PROPIEDAD", "HOTEL"), ValueAny(row, headers, "NUMERO DE RESERVA", "RESERVA"),
                ValueAny(row, headers, "PLAN DE COMIDAS", "REGIMEN"), ValueAny(row, headers, "FUENTE"),
                ValueAny(row, headers, "CONTACTO"), ValueAny(row, headers, "OBSERVACIONES"), row.RowNumber()));
        }
        return result.GroupBy(x => TextNormalizer.Normalize(x.Code)).Select(x => x.OrderByDescending(y => y.Row).First()).ToList();
    }

    private static List<PassengerRow> ParsePassengers(IXLWorksheet sheet, List<ImportIssue> issues)
    {
        var header = FindHeaderRow(sheet, "PASAJERO", "OPERADORA", "ESTADO HABITACION", "ESTADO TICKET DE VUELO");
        if (header is null) { issues.Add(new("Error", sheet.Name, null, "No se encontró el encabezado Pasajero.")); return []; }
        var headers = Headers(sheet, header.Value); WarnLegacyHeaders(headers, sheet.Name, issues);
        var result = new List<PassengerRow>();
        foreach (var row in sheet.RowsUsed().Where(x => x.RowNumber() > header.Value))
        {
            var name = ValueAny(row, headers, "PASAJERO"); if (string.IsNullOrWhiteSpace(name)) continue;
            if (ParseStatus(ValueAny(row, headers, "ESTADO TICKET DE VUELO")) == VerificationStatus.Confirmed)
                issues.Add(new("Advertencia", sheet.Name, row.RowNumber(), "Ticket confirmado sin datos estructurados: queda Por verificar hasta registrar PNR, aerolínea y estado individual confirmado."));
            if (ParseStatus(ValueAny(row, headers, "MALETA 23 KG INCLUIDA")) == VerificationStatus.Confirmed)
                issues.Add(new("Advertencia", sheet.Name, row.RowNumber(), "Equipaje confirmado sin datos estructurados: queda Por verificar hasta registrar reserva efectiva, cantidad, peso y cobertura."));
            result.Add(new(name, ValueAny(row, headers, "OPERADORA") ?? "", ValueAny(row, headers, "HABITACION / GRUPO"),
                ParseStatus(ValueAny(row, headers, "ESTADO DOCUMENTACION", "DOCUMENTACION")), ValueAny(row, headers, "PROXIMA ACCION"),
                ValueAny(row, headers, "OBSERVACIONES"), row.RowNumber()));
        }
        return result;
    }

    private static void WarnLegacyHeaders(Dictionary<string, int> headers, string sheet, List<ImportIssue> issues)
    {
        foreach (var header in LegacyHeaders.Where(headers.ContainsKey))
            issues.Add(new("Informacion", sheet, null, $"Columna heredada ignorada: {header}."));
    }
    private static int? FindHeaderRow(IXLWorksheet sheet, params string[] required)
    {
        foreach (var row in sheet.RowsUsed())
        {
            var cells = row.CellsUsed().Select(x => TextNormalizer.Normalize(x.GetFormattedString())).ToHashSet();
            if (required.All(cells.Contains)) return row.RowNumber();
        }
        return null;
    }
    private static int? FindFlexibleHeader(IXLWorksheet sheet, string[] possibleNames)
    {
        foreach (var row in sheet.RowsUsed())
        {
            var cells = row.CellsUsed().Select(x => TextNormalizer.Normalize(x.GetFormattedString())).ToHashSet();
            if (possibleNames.Any(x => cells.Contains(TextNormalizer.Normalize(x)))) return row.RowNumber();
        }
        return null;
    }
    private static Dictionary<string, int> Headers(IXLWorksheet sheet, int row) => sheet.Row(row).CellsUsed()
        .GroupBy(x => TextNormalizer.Normalize(x.GetFormattedString())).ToDictionary(x => x.Key, x => x.First().Address.ColumnNumber);
    private static string? ValueAny(IXLRow row, Dictionary<string, int> headers, params string[] keys)
    {
        foreach (var key in keys)
            if (headers.TryGetValue(TextNormalizer.Normalize(key), out var column))
            {
                var value = row.Cell(column).GetFormattedString().Trim();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        return null;
    }
    private static DateOnly? DateAny(IXLRow row, Dictionary<string, int> headers, params string[] keys)
    {
        foreach (var key in keys)
            if (headers.TryGetValue(TextNormalizer.Normalize(key), out var column))
            {
                var cell = row.Cell(column);
                if (cell.TryGetValue<DateTime>(out var value)) return DateOnly.FromDateTime(value);
                if (DateOnly.TryParse(cell.GetFormattedString(), out var parsed)) return parsed;
            }
        return null;
    }
    private static int IntAny(IXLRow row, Dictionary<string, int> headers, params string[] keys)
    {
        foreach (var key in keys)
            if (headers.TryGetValue(TextNormalizer.Normalize(key), out var column) && row.Cell(column).TryGetValue<int>(out var value)) return value;
        return 0;
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
        if (values[key] != expected) issues.Add(new("Advertencia", "Validación", null, $"Se esperaban {expected} {label}; el archivo contiene {values[key]}."));
    }
    private static bool RoomChanged(RoomReservation old, RoomRow row) => old.Status != row.Status || old.RoomType != row.Type
        || old.ExpectedCapacity != row.Capacity || old.CheckIn != row.CheckIn || old.CheckOut != row.CheckOut || old.Hotel != row.Hotel
        || old.HotelReservationNumber != row.ReservationNumber || old.MealPlan != row.MealPlan || old.SourceReference != row.Source || old.Notes != row.Notes;
    private static bool PassengerChanged(Passenger old, PassengerRow row) => old.FullName != row.Name.Trim()
        || old.DocumentationStatus != row.DocumentationStatus || old.NextAction != row.NextAction || old.Notes != row.Notes;
    private static void ApplyRoom(RoomReservation entity, RoomRow row, Operator op)
    {
        entity.InternalCode = row.Code.Trim(); entity.OperatorId = op.Id; entity.Status = row.Status; entity.RoomType = row.Type;
        entity.ExpectedCapacity = row.Capacity; entity.CheckIn = row.CheckIn; entity.CheckOut = row.CheckOut; entity.Hotel = row.Hotel;
        entity.HotelReservationNumber = row.ReservationNumber; entity.MealPlan = row.MealPlan; entity.SourceReference = row.Source;
        entity.OperatorContact = row.Contact; entity.Notes = row.Notes;
        entity.SpecificPropertyPending = BusinessRules.IsSpecificPropertyPending(op.Name, row.Hotel);
    }
    private static string? MergeNotes(string? current, string? extra) => string.IsNullOrWhiteSpace(extra) ? current
        : string.IsNullOrWhiteSpace(current) ? extra : current.Contains(extra, StringComparison.OrdinalIgnoreCase) ? current : $"{current}\n{extra}";
}
