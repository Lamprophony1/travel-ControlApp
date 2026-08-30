using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TravelControl.Application.Contracts;
using TravelControl.Application.Services;
using TravelControl.Domain;
using TravelControl.Infrastructure.Identity;
using TravelControl.Infrastructure.Persistence;

namespace TravelControl.Infrastructure.Services;

public sealed class DashboardService(AppDbContext db, TripReadinessService readiness, UserManager<AppUser> users)
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        ["passport"] = "Pasaporte", ["documentation"] = "Documentación", ["room"] = "Habitación",
        ["flight"] = "Ticket de vuelo", ["baggage"] = "Maleta de 23 kg"
    };

    public async Task<DashboardResponse> GetAsync(string? operatorName, string? overall, CancellationToken ct)
    {
        var snapshot = await readiness.GetAsync(ct);
        IEnumerable<PassengerReadiness> visible = snapshot.Passengers;
        if (!string.IsNullOrWhiteSpace(operatorName))
            visible = visible.Where(x => x.Passenger.PrimaryOperator?.Name == operatorName);
        if (Enum.TryParse<PassengerOverallStatus>(overall, true, out var filterStatus))
            visible = visible.Where(x => x.State.OverallStatus == filterStatus);
        var states = visible.ToArray();
        var total = snapshot.TotalPassengers;
        int Percent(int value, int denominator) => denominator == 0 ? 0 : (int)Math.Round(value * 100m / denominator);
        DashboardKpi Kpi(string key, string label, int value, int denominator, string filter) =>
            new(key, label, value, denominator, Percent(value, denominator), filter);
        var kpis = new[]
        {
            Kpi("ready", "Pasajeros listos", snapshot.ReadyPassengers, total, "overall=Ready"),
            Kpi("pending", "Pasajeros pendientes", snapshot.PendingPassengers, total, "overall=Pending"),
            Kpi("attention", "Pasajeros en atención", snapshot.AttentionPassengers, total, "overall=Attention"),
            Kpi("accommodationPassengers", "Pasajeros con alojamiento resuelto", snapshot.AccommodationPassengersResolved, total, "requirement=room"),
            Kpi("flights", "Tickets resueltos", snapshot.Requirements["flight"].Resolved, total, "requirement=flight"),
            Kpi("baggage", "Maletas resueltas", snapshot.Requirements["baggage"].Resolved, total, "requirement=baggage"),
            Kpi("documentation", "Documentaciones resueltas", snapshot.Requirements["documentation"].Resolved, total, "requirement=documentation"),
            Kpi("passports", "Pasaportes completos", snapshot.Requirements["passport"].Resolved, total, "requirement=passport"),
            Kpi("roomsConfirmed", "Habitaciones confirmadas", snapshot.RoomsConfirmed, snapshot.Rooms.Count, "/gestion/habitaciones")
        };
        var categories = Labels.Select(pair =>
        {
            var value = snapshot.Requirements[pair.Key];
            return new CategoryProgress(pair.Key, pair.Value, value.Confirmed, value.Pending, value.InProgress,
                value.NotIncluded, value.NotApplicable, Percent(value.Resolved, total));
        }).ToArray();
        var operators = snapshot.Passengers.GroupBy(x => x.Passenger.PrimaryOperator?.Name ?? "Sin operadora").Select(group =>
        {
            var rooms = group.Select(x => x.Passenger.RoomReservation).Where(x => x is not null).DistinctBy(x => x!.Id).ToArray();
            var resolvedRoomIds = snapshot.Rooms.Where(room => snapshot.Passengers.Any(p => p.Passenger.RoomReservationId == room.Id
                    && BusinessRules.IsResolved(p.State.Requirements.Single(r => r.Key == "room"))))
                .Select(x => x.Id).ToHashSet();
            return new OperatorSummary(group.Key, rooms.Length, group.Count(), rooms.Count(x => resolvedRoomIds.Contains(x!.Id)),
                group.SelectMany(x => x.State.Alerts).Distinct().ToArray());
        }).OrderBy(x => x.Name).ToArray();
        var transferUser = snapshot.Transfer.UpdatedByUserId.HasValue
            ? await users.FindByIdAsync(snapshot.Transfer.UpdatedByUserId.Value.ToString()) : null;
        var transfer = new TransferStatusResponse(snapshot.Transfer.IsConfirmed, snapshot.Transfer.ConfirmedAt, snapshot.Transfer.Notes,
            transferUser?.DisplayName, snapshot.Transfer.UpdatedAt, snapshot.Transfer.Version);
        var actions = snapshot.Blockers.Select(x => new PriorityAction(x.Severity, x.Message, 1, x.Key)).ToList();
        actions.AddRange(Labels.Where(x => snapshot.Requirements[x.Key].Resolved < total)
            .Select(x => new PriorityAction("warning", $"{x.Value}: pendientes", total - snapshot.Requirements[x.Key].Resolved, $"requirement={x.Key}")));
        var auditEntries = await db.AuditLogs
            .FromSqlRaw("SELECT * FROM AuditLogs ORDER BY At DESC LIMIT 12")
            .AsNoTracking()
            .ToListAsync(ct);
        var activity = auditEntries.Select(x => new RecentActivity(x.Id, null, x.EntityName + " · " + x.Action,
            x.UserName, x.At, x.PreviousValue, x.NewValue)).ToArray();
        var tripState = new TripComputedState(snapshot.OverallStatus, snapshot.ProgressPercent,
            snapshot.ReadyPassengers == snapshot.TotalPassengers && snapshot.TotalPassengers > 0,
            snapshot.Transfer.IsConfirmed, snapshot.Blockers.Select(x => x.Message).ToArray());
        return new(kpis, categories,
            new Dictionary<string, int> { ["ready"] = snapshot.ReadyPassengers, ["pending"] = snapshot.PendingPassengers, ["attention"] = snapshot.AttentionPassengers },
            operators, actions, activity, transfer, tripState,
            snapshot.RoomsConfirmed, snapshot.RoomsPending, snapshot.SpecificPropertiesPending);
    }
}
