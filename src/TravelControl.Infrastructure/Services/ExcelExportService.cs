using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using TravelControl.Application.Services;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;

namespace TravelControl.Infrastructure.Services;

public sealed class ExcelExportService(AppDbContext db, PassengerQueryService passengerQueries, EvidenceResolver evidenceResolver)
{
    private static readonly XLColor Navy = XLColor.FromHtml("#12304A");
    private static readonly XLColor Turquoise = XLColor.FromHtml("#008A8C");

    public async Task<byte[]> ExportAsync(CancellationToken ct)
    {
        var passengers = await passengerQueries.BaseQuery().OrderBy(x => x.FullName).ToListAsync(ct);
        var rooms = await db.RoomReservations.AsNoTracking().Include(x => x.Operator).Include(x => x.Passengers).OrderBy(x => x.InternalCode).ToListAsync(ct);
        var transfer = await db.TripTransferStatuses.AsNoTracking().SingleAsync(x => x.Trip.IsActive, ct);
        var evidence = await evidenceResolver.GetForPassengersAsync(passengers.Select(x => x.Id), ct);
        var roomEvidence = await evidenceResolver.GetRoomEvidenceAsync(rooms.Select(x => x.Id), ct);
        using var workbook = new XLWorkbook();
        BuildDashboard(workbook.AddWorksheet("Dashboard"), passengers, rooms, transfer, evidence, roomEvidence);
        BuildPassengers(workbook.AddWorksheet("Control pasajeros"), passengers, evidence);
        BuildRooms(workbook.AddWorksheet("Habitaciones"), rooms, roomEvidence);
        BuildSources(workbook.AddWorksheet("Fuentes y uso"));
        using var stream = new MemoryStream(); workbook.SaveAs(stream); return stream.ToArray();
    }

    public async Task<string> ExportBackupJsonAsync(CancellationToken ct)
    {
        var data = new { exportedAt = DateTimeOffset.UtcNow, trips = await db.Trips.AsNoTracking().ToListAsync(ct),
            tripTransferStatuses = await db.TripTransferStatuses.AsNoTracking().ToListAsync(ct), operators = await db.Operators.AsNoTracking().ToListAsync(ct),
            passengers = await db.Passengers.AsNoTracking().ToListAsync(ct), rooms = await db.RoomReservations.AsNoTracking().ToListAsync(ct),
            flights = await db.FlightBookings.AsNoTracking().Include(x => x.Segments).Include(x => x.PassengerFlights).ToListAsync(ct),
            baggage = await db.BaggageEntitlements.AsNoTracking().ToListAsync(ct), followUps = await db.FollowUps.AsNoTracking().ToListAsync(ct) };
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<byte[]> ExportPassengersCsvAsync(CancellationToken ct)
    {
        var people = await passengerQueries.BaseQuery().OrderBy(x => x.FullName).ToListAsync(ct);
        var lines = new List<string> { "Nombre,Pasaporte enmascarado,Operadora,Código interno de grupo,Estado general,Avance,Próxima acción,Fecha próxima acción" };
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var evidence = await evidenceResolver.GetForPassengersAsync(people.Select(x => x.Id), ct);
        foreach (var passenger in people)
        {
            var state = BusinessRules.CalculatePassenger(passenger, today, evidence.GetValueOrDefault(passenger.Id) ?? new PassengerEvidenceState());
            lines.Add(string.Join(',', new[] { passenger.FullName, PassengerQueryService.MaskPassport(passenger.PassportNumber), passenger.PrimaryOperator?.Name,
                passenger.RoomReservation?.InternalCode, OverallLabel(state.OverallStatus), state.ProgressPercent.ToString(), passenger.NextAction,
                passenger.NextActionDueDate?.ToString("dd/MM/yyyy") }.Select(Csv)));
        }
        return Encoding.UTF8.GetBytes(string.Join("\r\n", lines));
    }

    public async Task<byte[]> ExportPendingAsync(CancellationToken ct)
    {
        var people = await passengerQueries.BaseQuery().OrderBy(x => x.FullName).ToListAsync(ct);
        var transfer = await db.TripTransferStatuses.AsNoTracking().SingleAsync(x => x.Trip.IsActive, ct);
        using var workbook = new XLWorkbook(); var sheet = workbook.AddWorksheet("Pendientes");
        Title(sheet, "Pendientes del viaje", 5); Row(sheet, 2, "Alcance", "Elemento", "Requisito", "Estado", "Próxima acción"); Header(sheet.Range(2, 1, 2, 5));
        var row = 3;
        if (!transfer.IsConfirmed) Row(sheet, row++, "Global", "Transfer grupal", "Confirmación única", "Pendiente", transfer.Notes);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var evidence = await evidenceResolver.GetForPassengersAsync(people.Select(x => x.Id), ct);
        foreach (var passenger in people)
            foreach (var requirement in BusinessRules.CalculatePassenger(passenger, today,
                evidence.GetValueOrDefault(passenger.Id) ?? new PassengerEvidenceState()).Requirements.Where(x => !BusinessRules.IsResolved(x)))
                Row(sheet, row++, "Pasajero", passenger.FullName, requirement.Label, VerificationLabel(requirement.Status), passenger.NextAction);
        sheet.Columns().AdjustToContents(12, 42); using var stream = new MemoryStream(); workbook.SaveAs(stream); return stream.ToArray();
    }

    private static void BuildDashboard(IXLWorksheet sheet, IReadOnlyList<Passenger> passengers, IReadOnlyList<RoomReservation> rooms,
        TripTransferStatus transfer, IReadOnlyDictionary<Guid, PassengerEvidenceState> evidence, IReadOnlySet<Guid> roomEvidence)
    {
        Title(sheet, "Estado general del viaje", 4); Row(sheet, 3, "Indicador", "Valor", "Total", "%"); Header(sheet.Range(3, 1, 3, 4));
        var states = passengers.Select(x => BusinessRules.CalculatePassenger(x, DateOnly.FromDateTime(DateTime.UtcNow),
            evidence.GetValueOrDefault(x.Id) ?? new PassengerEvidenceState())).ToArray();
        var trip = BusinessRules.CalculateTrip(states, transfer.IsConfirmed); var ready = states.Count(x => x.OverallStatus == PassengerOverallStatus.Ready);
        Row(sheet, 4, "Pasajeros", passengers.Count, passengers.Count, passengers.Count == 0 ? 0 : 100);
        Row(sheet, 5, "Pasajeros listos", ready, passengers.Count, Percent(ready, passengers.Count));
        var resolvedAccommodation = states.Count(x => BusinessRules.IsResolved(x.Requirements.Single(r => r.Key == "room")));
        Row(sheet, 6, "Pasajeros con alojamiento resuelto", resolvedAccommodation, passengers.Count, Percent(resolvedAccommodation, passengers.Count));
        var resolvedRooms = rooms.Count(x => x.Status == VerificationStatus.Confirmed && BusinessRules.RoomCanBeConfirmed(x, roomEvidence.Contains(x.Id), out _)
            || x.Status == VerificationStatus.NotApplicable && !string.IsNullOrWhiteSpace(x.Notes));
        Row(sheet, 7, "Habitaciones confirmadas", resolvedRooms, rooms.Count, Percent(resolvedRooms, rooms.Count));
        Row(sheet, 8, "Propiedades específicas pendientes", rooms.Count(x => x.SpecificPropertyPending), rooms.Count, Percent(rooms.Count(x => !x.SpecificPropertyPending), rooms.Count));
        Row(sheet, 9, "Progreso global", trip.ProgressPercent, 100, trip.ProgressPercent);
        Row(sheet, 10, "Transfer grupal", transfer.IsConfirmed ? "Confirmado" : "Pendiente", "Único", transfer.IsConfirmed ? 100 : 0);
        sheet.Columns().AdjustToContents(12, 38); sheet.SheetView.FreezeRows(3);
    }

    private static void BuildPassengers(IXLWorksheet sheet, IReadOnlyList<Passenger> passengers, IReadOnlyDictionary<Guid, PassengerEvidenceState> evidence)
    {
        var headers = new[] { "Pasajero", "Estado pasaporte", "Operadora", "Habitación / grupo", "Pasaporte enmascarado", "Documentación", "Habitación", "Vuelo", "Maleta 23 kg", "Avance", "Próxima acción", "Fecha próxima acción", "Observaciones" };
        Row(sheet, 1, headers.Cast<object?>().ToArray()); Header(sheet.Range(1, 1, 1, headers.Length)); var today = DateOnly.FromDateTime(DateTime.UtcNow);
        for (var index = 0; index < passengers.Count; index++)
        {
            var p = passengers[index]; var state = BusinessRules.CalculatePassenger(p, today,
                evidence.GetValueOrDefault(p.Id) ?? new PassengerEvidenceState()); var row = index + 2;
            Row(sheet, row, p.FullName, Status(state, "passport"), p.PrimaryOperator?.Name, p.RoomReservation?.InternalCode,
                PassengerQueryService.MaskPassport(p.PassportNumber), Status(state, "documentation"), Status(state, "room"), Status(state, "flight"), Status(state, "baggage"),
                state.ProgressPercent, p.NextAction, p.NextActionDueDate, p.Notes); sheet.Cell(row, 12).Style.DateFormat.Format = "dd/mm/yyyy";
        }
        if (passengers.Count > 0) sheet.Range(1, 1, passengers.Count + 1, headers.Length).CreateTable("PassengerControl");
        sheet.SheetView.FreezeRows(1); sheet.Columns().AdjustToContents(10, 34);
    }

    private static void BuildRooms(IXLWorksheet sheet, IReadOnlyList<RoomReservation> rooms, IReadOnlySet<Guid> evidence)
    {
        var headers = new[] { "Código interno de grupo", "Operadora", "Estado", "Hotel / propiedad", "Tipo habitación", "Ocupantes", "Capacidad", "Check-in", "Check-out", "Noches", "Reserva", "Plan", "Fuente", "Contacto", "Observaciones" };
        Row(sheet, 1, headers.Cast<object?>().ToArray()); Header(sheet.Range(1, 1, 1, headers.Length));
        for (var index = 0; index < rooms.Count; index++)
        {
            var room = rooms[index]; var row = index + 2;
            var effectiveStatus = room.Status == VerificationStatus.Confirmed && !BusinessRules.RoomCanBeConfirmed(room, evidence.Contains(room.Id), out _)
                ? VerificationStatus.ToVerify : room.Status;
            Row(sheet, row, room.InternalCode, room.Operator.Name, VerificationLabel(effectiveStatus), room.Hotel, room.RoomType,
                room.Passengers.Count, room.ExpectedCapacity, room.CheckIn, room.CheckOut, room.Nights, room.HotelReservationNumber, room.MealPlan, room.SourceReference, room.OperatorContact, room.Notes);
            sheet.Range(row, 8, row, 9).Style.DateFormat.Format = "dd/mm/yyyy";
        }
        if (rooms.Count > 0) sheet.Range(1, 1, rooms.Count + 1, headers.Length).CreateTable("RoomControl"); sheet.SheetView.FreezeRows(1); sheet.Columns().AdjustToContents(10, 34);
    }

    private static void BuildSources(IXLWorksheet sheet)
    {
        Title(sheet, "Fuentes y uso", 3); Row(sheet, 3, "Hoja", "Uso", "Importable"); Header(sheet.Range("A3:C3"));
        Row(sheet, 4, "Control pasajeros", "Fuente autoritativa de pasajeros y estado operativo", "Sí");
        Row(sheet, 5, "Habitaciones", "Fuente autoritativa de reservas y ocupación", "Sí");
        Row(sheet, 6, "Dashboard", "Resumen calculado; nunca se importa", "No"); Row(sheet, 7, "Fuentes y uso", "Documentación informativa", "No");
        sheet.Columns().AdjustToContents(12, 55);
    }

    private static void Row(IXLWorksheet sheet, int row, params object?[] values)
    { for (var index = 0; index < values.Length; index++) sheet.Cell(row, index + 1).Value = XLCellValue.FromObject(values[index]); }
    private static void Title(IXLWorksheet sheet, string value, int columns) { sheet.Cell(1, 1).Value = value; sheet.Range(1, 1, 1, columns).Merge(); var range = sheet.Range(1, 1, 1, columns); range.Style.Fill.BackgroundColor = Navy; range.Style.Font.FontColor = XLColor.White; range.Style.Font.Bold = true; range.Style.Font.FontSize = 16; }
    private static void Header(IXLRange range) { range.Style.Fill.BackgroundColor = Turquoise; range.Style.Font.FontColor = XLColor.White; range.Style.Font.Bold = true; }
    private static string Status(PassengerComputedState state, string key) => VerificationLabel(state.Requirements.Single(x => x.Key == key).Status);
    private static string VerificationLabel(VerificationStatus status) => status switch
    {
        VerificationStatus.Confirmed => "Confirmado",
        VerificationStatus.ToVerify => "Por verificar",
        VerificationStatus.InProgress => "En gestión",
        VerificationStatus.NotIncluded => "No incluido",
        VerificationStatus.NotApplicable => "No aplica",
        _ => status.ToString()
    };
    private static string OverallLabel(PassengerOverallStatus status) => status switch
    {
        PassengerOverallStatus.Ready => "Listo",
        PassengerOverallStatus.Pending => "Pendiente",
        PassengerOverallStatus.Attention => "Atención",
        _ => status.ToString()
    };
    private static int Percent(int value, int total) => total == 0 ? 0 : (int)Math.Round(value * 100m / total);
    private static string Csv(string? value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";
}
