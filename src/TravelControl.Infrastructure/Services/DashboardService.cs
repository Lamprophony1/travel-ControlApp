using Microsoft.EntityFrameworkCore;
using TravelControl.Api.Contracts;
using TravelControl.Api.Data;
using TravelControl.Api.Domain;

namespace TravelControl.Api.Services;

public sealed class DashboardService(AppDbContext db, PassengerQueryService passengers)
{
    public async Task<DashboardResponse> GetAsync(string? operatorName, string? overall, string? owner, CancellationToken ct)
    {
        var query = passengers.BaseQuery().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(operatorName)) query = query.Where(x => x.PrimaryOperator != null && x.PrimaryOperator.Name == operatorName);
        if (!string.IsNullOrWhiteSpace(owner)) query = query.Where(x => x.InternalOwner == owner);
        var entities = await query.ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var states = entities.Select(x => (Passenger: x, State: BusinessRules.CalculatePassenger(x, today))).ToList();
        if (Enum.TryParse<PassengerOverallStatus>(overall, true, out var filterStatus)) states = states.Where(x => x.State.OverallStatus == filterStatus).ToList();
        var total = states.Count;
        var rooms = states.Select(x => x.Passenger.RoomReservation).Where(x => x != null).DistinctBy(x => x!.Id).ToList();

        int CountReq(string key) => states.Count(x => x.State.Requirements.Single(r => r.Key == key).Status == VerificationStatus.Confirmed);
        int Percent(int value) => total == 0 ? 0 : (int)Math.Round(value * 100m / total);
        DashboardKpi Kpi(string key, string label, int value, string filter) => new(key, label, value, total, Percent(value), filter);
        var ready = states.Count(x => x.State.OverallStatus == PassengerOverallStatus.Ready);
        var pending = states.Count(x => x.State.OverallStatus == PassengerOverallStatus.Pending);
        var attention = states.Count(x => x.State.OverallStatus == PassengerOverallStatus.Attention);
        var roomConfirmedPassengers = CountReq("room");
        var kpis = new List<DashboardKpi>
        {
            new("passengers", "Total de pasajeros", total, total, total == 0 ? 0 : 100, ""),
            Kpi("roomsConfirmed", "Habitaciones confirmadas", roomConfirmedPassengers, "requirement=room&status=Confirmed"),
            Kpi("ready", "Listos para viajar", ready, "overall=Ready"),
            Kpi("pending", "Con pendientes", pending, "overall=Pending"),
            Kpi("attention", "En atención", attention, "overall=Attention"),
            Kpi("flights", "Tickets verificados", CountReq("flight"), "requirement=flight&status=Confirmed"),
            Kpi("baggage", "Maletas 23 kg verificadas", CountReq("baggage"), "requirement=baggage&status=Confirmed"),
            Kpi("transfers", "Transfers verificados", CountReq("transfer"), "requirement=transfer&status=Confirmed"),
            Kpi("documentation", "Documentaciones verificadas", CountReq("documentation"), "requirement=documentation&status=Confirmed"),
            Kpi("passports", "Pasaportes completos", CountReq("passport"), "requirement=passport&status=Confirmed"),
            new("rooms", "Total de habitaciones", rooms.Count, rooms.Count, rooms.Count == 0 ? 0 : 100, "/rooms")
        };

        var labels = new Dictionary<string, string> { ["room"]="Habitación", ["flight"]="Ticket de vuelo", ["baggage"]="Maleta de 23 kg", ["transfer"]="Transfer", ["documentation"]="Documentación", ["passport"]="Pasaporte" };
        var categories = labels.Select(pair =>
        {
            var req = states.Select(x => x.State.Requirements.Single(r => r.Key == pair.Key)).ToList();
            var resolved = req.Count(BusinessRules.IsResolved);
            return new CategoryProgress(pair.Key, pair.Value, req.Count(x => x.Status == VerificationStatus.Confirmed),
                req.Count(x => x.Status == VerificationStatus.ToVerify), req.Count(x => x.Status == VerificationStatus.InProgress),
                req.Count(x => x.Status == VerificationStatus.NotIncluded), req.Count(x => x.Status == VerificationStatus.NotApplicable), Percent(resolved));
        }).ToList();

        var opSummary = states.GroupBy(x => x.Passenger.PrimaryOperator?.Name ?? "Sin operadora").Select(group =>
        {
            var opRooms = group.Select(x => x.Passenger.RoomReservation).Where(x => x != null).DistinctBy(x => x!.Id).ToList();
            var alerts = group.SelectMany(x => x.State.Alerts).Distinct().ToList();
            return new OperatorSummary(group.Key, opRooms.Count, group.Count(), opRooms.Count(x => x!.Status == VerificationStatus.Confirmed), alerts);
        }).OrderBy(x => x.Name).ToList();

        var actions = new List<PriorityAction>
        {
            new("critical", "Pasajeros con alertas críticas", attention, "overall=Attention"),
            new("warning", "Tickets de vuelo pendientes", total - CountReq("flight"), "requirement=flight"),
            new("warning", "Maletas de 23 kg pendientes", total - CountReq("baggage"), "requirement=baggage"),
            new("warning", "Transfers pendientes", total - CountReq("transfer"), "requirement=transfer"),
            new("info", "Documentaciones pendientes", total - CountReq("documentation"), "requirement=documentation"),
            new("info", "Propiedades Top Travel por completar", states.Count(x => x.Passenger.RoomReservation?.SpecificPropertyPending == true), "propertyPending=true")
        }.Where(x => x.Count > 0).OrderBy(x => x.Severity == "critical" ? 0 : x.Severity == "warning" ? 1 : 2).ToList();

        var activity = await db.AuditLogs.AsNoTracking().OrderByDescending(x => x.At).Take(12)
            .Select(x => new RecentActivity(x.Id, null, x.EntityName + " · " + x.Action, x.UserName, x.At, x.PreviousValue, x.NewValue)).ToListAsync(ct);
        return new DashboardResponse(kpis, categories,
            new Dictionary<string, int> { ["ready"] = ready, ["pending"] = pending, ["attention"] = attention },
            opSummary, actions, activity);
    }
}
