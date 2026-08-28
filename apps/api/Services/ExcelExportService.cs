using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TravelControl.Api.Data;
using TravelControl.Api.Domain;

namespace TravelControl.Api.Services;

public sealed class ExcelExportService(AppDbContext db, PassengerQueryService passengerQueries)
{
    private static readonly XLColor Navy = XLColor.FromHtml("#12304A");
    private static readonly XLColor Turquoise = XLColor.FromHtml("#00A6A6");

    public async Task<byte[]> ExportAsync(CancellationToken ct)
    {
        var passengers = await passengerQueries.BaseQuery().AsNoTracking().OrderBy(x => x.FullName).ToListAsync(ct);
        var rooms = await db.RoomReservations.AsNoTracking().Include(x => x.Operator).Include(x => x.Passengers).OrderBy(x => x.InternalCode).ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        using var wb = new XLWorkbook();
        BuildDashboard(wb.AddWorksheet("Dashboard"), passengers, rooms, today);
        BuildPassengers(wb.AddWorksheet("Control pasajeros"), passengers, today);
        BuildRooms(wb.AddWorksheet("Habitaciones"), rooms);
        BuildSources(wb.AddWorksheet("Fuentes y uso"));
        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<string> ExportBackupJsonAsync(CancellationToken ct)
    {
        var data = new
        {
            exportedAt = DateTimeOffset.UtcNow,
            trips = await db.Trips.AsNoTracking().ToListAsync(ct),
            operators = await db.Operators.AsNoTracking().ToListAsync(ct),
            passengers = await db.Passengers.AsNoTracking().ToListAsync(ct),
            rooms = await db.RoomReservations.AsNoTracking().ToListAsync(ct),
            flights = await db.FlightBookings.AsNoTracking().Include(x => x.Segments).Include(x => x.PassengerFlights).ToListAsync(ct),
            baggage = await db.BaggageEntitlements.AsNoTracking().ToListAsync(ct),
            transfers = await db.TransferBookings.AsNoTracking().Include(x => x.PassengerTransfers).ToListAsync(ct),
            followUps = await db.FollowUps.AsNoTracking().ToListAsync(ct)
        };
        return System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<byte[]> ExportPassengersCsvAsync(CancellationToken ct)
    {
        var people = await passengerQueries.BaseQuery().AsNoTracking().OrderBy(x => x.FullName).ToListAsync(ct);
        var lines = new List<string> { "Nombre,Pasaporte,Operadora,Código interno de grupo,Estado general,Avance,Próxima acción,Responsable" };
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var p in people)
        {
            var state = BusinessRules.CalculatePassenger(p, today);
            lines.Add(string.Join(',', new[] { p.FullName, PassengerQueryService.MaskPassport(p.PassportNumber), p.PrimaryOperator?.Name, p.RoomReservation?.InternalCode,
                StatusLabel(state.OverallStatus), state.ProgressPercent.ToString(System.Globalization.CultureInfo.InvariantCulture), p.NextAction, p.InternalOwner }.Select(Csv)));
        }
        return System.Text.Encoding.UTF8.GetBytes(string.Join("\r\n", lines));
    }

    public async Task<byte[]> ExportPendingAsync(CancellationToken ct)
    {
        var people = await passengerQueries.BaseQuery().AsNoTracking().OrderBy(x => x.FullName).ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        using var wb = new XLWorkbook(); var ws = wb.AddWorksheet("Pendientes");
        var headers = new[] { "Pasajero", "Estado general", "Avance", "Pendientes", "Alertas", "Próxima acción", "Responsable", "Fecha límite" };
        ws.Cell(1, 1).InsertData(new[] { headers }); var row = 2;
        foreach (var p in people)
        {
            var state = BusinessRules.CalculatePassenger(p, today); if (state.OverallStatus == PassengerOverallStatus.Ready) continue;
            ws.Cell(row++, 1).InsertData(new[] { new object?[] { p.FullName, StatusLabel(state.OverallStatus), state.ProgressPercent / 100m,
                string.Join(", ", state.Requirements.Where(x => !BusinessRules.IsResolved(x)).Select(x => x.Label)), string.Join(" · ", state.Alerts),
                p.NextAction, p.InternalOwner, p.NextActionDueDate?.ToDateTime(TimeOnly.MinValue) } });
        }
        StyleHeader(ws.Range(1, 1, 1, headers.Length)); ws.Range(1, 1, Math.Max(2, row - 1), headers.Length).CreateTable("PendingExport");
        ws.SheetView.FreezeRows(1); ws.RangeUsed()!.SetAutoFilter(); ws.Columns(1, headers.Length).AdjustToContents(1, Math.Min(row, 60));
        foreach (var column in new[] { 4, 5, 6 }) ws.Column(column).Width = 36;
        ws.Column(3).Style.NumberFormat.Format = "0%"; ws.Column(8).Style.DateFormat.Format = "dd/MM/yyyy";
        using var stream = new MemoryStream(); wb.SaveAs(stream); return stream.ToArray();
    }

    private static void BuildDashboard(IXLWorksheet ws, List<Passenger> passengers, List<RoomReservation> rooms, DateOnly today)
    {
        ws.Cell("A1").Value = "CONTROL DE VIAJE — BODA CIELITO & RONALDO";
        ws.Range("A1:F1").Merge().Style.Fill.BackgroundColor = Navy;
        ws.Range("A1:F1").Style.Font.SetBold().Font.SetFontColor(XLColor.White).Font.SetFontSize(16);
        var states = passengers.Select(x => BusinessRules.CalculatePassenger(x, today)).ToList();
        var values = new object?[,]
        {
            { "Indicador", "Cantidad", "Total", "%", null, null },
            { "Pasajeros", passengers.Count, passengers.Count, passengers.Count == 0 ? 0 : 1m, null, null },
            { "Habitaciones confirmadas", rooms.Count(x => x.Status == VerificationStatus.Confirmed), rooms.Count, rooms.Count == 0 ? 0 : rooms.Count(x => x.Status == VerificationStatus.Confirmed)/(decimal)rooms.Count, null, null },
            { "Listos", states.Count(x => x.OverallStatus == PassengerOverallStatus.Ready), passengers.Count, passengers.Count == 0 ? 0 : states.Count(x => x.OverallStatus == PassengerOverallStatus.Ready)/(decimal)passengers.Count, null, null },
            { "Pendientes", states.Count(x => x.OverallStatus == PassengerOverallStatus.Pending), passengers.Count, passengers.Count == 0 ? 0 : states.Count(x => x.OverallStatus == PassengerOverallStatus.Pending)/(decimal)passengers.Count, null, null },
            { "Atención", states.Count(x => x.OverallStatus == PassengerOverallStatus.Attention), passengers.Count, passengers.Count == 0 ? 0 : states.Count(x => x.OverallStatus == PassengerOverallStatus.Attention)/(decimal)passengers.Count, null, null }
        };
        ws.Cell("A3").InsertData(values);
        StyleHeader(ws.Range("A3:D3")); ws.Column(1).Width = 28; ws.Columns(2, 4).Width = 14;
        ws.Range("D4:D8").Style.NumberFormat.Format = "0%";
        ws.SheetView.FreezeRows(3); ws.RangeUsed()!.SetAutoFilter();
    }

    private static void BuildPassengers(IXLWorksheet ws, List<Passenger> passengers, DateOnly today)
    {
        var headers = new[] { "ID", "Operadora", "Código interno de grupo", "Pasajero", "Pasaporte", "Estado pasaporte", "Hotel / propiedad", "Tipo habitación", "Check-in", "Check-out", "Noches", "Habitación", "Ticket", "Maleta 23 kg", "Transfer", "Documentación", "Estado general", "Avance", "Pendientes", "Próxima acción", "Responsable", "Última actualización", "Observaciones" };
        ws.Cell(1, 1).InsertData(new[] { headers });
        var row = 2;
        foreach (var p in passengers)
        {
            var s = BusinessRules.CalculatePassenger(p, today);
            var req = s.Requirements.ToDictionary(x => x.Key);
            var data = new object?[] { p.Id, p.PrimaryOperator?.Name, p.RoomReservation?.InternalCode, p.FullName,
                PassengerQueryService.MaskPassport(p.PassportNumber), StatusLabel(s.PassportStatus), p.RoomReservation?.Hotel, p.RoomReservation?.RoomType,
                p.RoomReservation?.CheckIn?.ToDateTime(TimeOnly.MinValue), p.RoomReservation?.CheckOut?.ToDateTime(TimeOnly.MinValue), p.RoomReservation?.Nights,
                StatusLabel(req["room"].Status), StatusLabel(req["flight"].Status), StatusLabel(req["baggage"].Status), StatusLabel(req["transfer"].Status),
                StatusLabel(req["documentation"].Status), StatusLabel(s.OverallStatus), s.ProgressPercent / 100m,
                string.Join(", ", req.Values.Where(x => !BusinessRules.IsResolved(x)).Select(x => x.Label)), p.NextAction, p.InternalOwner,
                p.UpdatedAt.DateTime, p.Notes };
            ws.Cell(row++, 1).InsertData(new[] { data });
        }
        StyleHeader(ws.Range(1, 1, 1, headers.Length));
        ws.Range(1, 1, Math.Max(2, row - 1), headers.Length).CreateTable("PassengersExport");
        ws.SheetView.FreezeRows(1); ws.RangeUsed()!.SetAutoFilter();
        ws.Columns(1, headers.Length).AdjustToContents(1, Math.Min(row, 60));
        foreach (var c in new[] { 5, 7, 19, 20, 23 }) ws.Column(c).Width = Math.Min(ws.Column(c).Width, 38);
        ws.Columns(9, 10).Style.DateFormat.Format = "dd/MM/yyyy"; ws.Column(22).Style.DateFormat.Format = "dd/MM/yyyy HH:mm"; ws.Column(18).Style.NumberFormat.Format = "0%";
    }

    private static void BuildRooms(IXLWorksheet ws, List<RoomReservation> rooms)
    {
        var headers = new[] { "Código interno de grupo", "Operadora", "Estado", "Hotel / propiedad", "Tipo", "Check-in", "Check-out", "Noches", "Ocupantes", "Capacidad", "Fuente", "Reserva", "Contacto", "Alertas", "Observaciones" };
        ws.Cell(1, 1).InsertData(new[] { headers }); var row = 2;
        foreach (var r in rooms)
        {
            ws.Cell(row++, 1).InsertData(new[] { new object?[] { r.InternalCode, r.Operator.Name, StatusLabel(r.Status), r.Hotel, r.RoomType,
                r.CheckIn?.ToDateTime(TimeOnly.MinValue), r.CheckOut?.ToDateTime(TimeOnly.MinValue), r.Nights, r.Passengers.Count, r.ExpectedCapacity,
                r.SourceReference, r.HotelReservationNumber, r.OperatorContact, r.SpecificPropertyPending ? BusinessRules.TopTravelPropertyAlert : null, r.Notes } });
        }
        StyleHeader(ws.Range(1, 1, 1, headers.Length)); ws.Range(1, 1, Math.Max(2, row - 1), headers.Length).CreateTable("RoomsExport");
        ws.SheetView.FreezeRows(1); ws.RangeUsed()!.SetAutoFilter(); ws.Columns(1, headers.Length).AdjustToContents(1, Math.Min(row, 40));
        foreach (var column in new[] { 4, 11, 15 }) ws.Column(column).Width = 35;
        ws.Columns(6, 7).Style.DateFormat.Format = "dd/MM/yyyy";
    }

    private static void BuildSources(IXLWorksheet ws)
    {
        ws.Cell("A1").Value = "FUENTES Y CRITERIOS"; ws.Range("A1:D1").Merge(); StyleHeader(ws.Range("A1:D1"));
        ws.Cell("A3").InsertData(new[] { new[] { "Elemento", "Criterio" }, new[] { "Pasajeros", "Control pasajeros es la fuente autoritativa." },
            new[] { "Habitaciones", "Habitaciones es la fuente autoritativa." }, new[] { "Dashboard", "Valores recalculados desde la base de datos; no se importan métricas." },
            new[] { "Pasaportes", "Control preventivo interno. Verificar requisitos migratorios oficiales antes del viaje." } });
        StyleHeader(ws.Range("A3:B3")); ws.Column(1).Width = 24; ws.Column(2).Width = 80; ws.Column(2).Style.Alignment.WrapText = true;
    }

    private static void StyleHeader(IXLRange range)
    {
        range.Style.Fill.BackgroundColor = Turquoise; range.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }
    private static string StatusLabel(Enum status) => status.ToString() switch
    {
        "Confirmed" => "Confirmado", "ToVerify" => "Por verificar", "InProgress" => "En gestión", "NotIncluded" => "No incluido", "NotApplicable" => "No aplica",
        "Ready" => "Listo", "Pending" => "Pendiente", "Attention" => "Atención", "Incomplete" => "Incompleto", "Expired" => "Vencido", "ExpiringSoon" => "Por vencer", "Valid" => "Vigente", _ => status.ToString()
    };
    private static string Csv(string? value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";
}
