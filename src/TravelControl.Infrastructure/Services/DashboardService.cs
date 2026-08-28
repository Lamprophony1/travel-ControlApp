using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TravelControl.Application.Contracts;
using TravelControl.Application.Services;
using TravelControl.Domain;
using TravelControl.Infrastructure.Identity;
using TravelControl.Infrastructure.Persistence;

namespace TravelControl.Infrastructure.Services;

public sealed class DashboardService(AppDbContext db, PassengerQueryService passengers, UserManager<AppUser> users)
{
    public async Task<DashboardResponse> GetAsync(string? operatorName, string? overall, CancellationToken ct)
    {
        var query = passengers.BaseQuery();
        if (!string.IsNullOrWhiteSpace(operatorName)) query = query.Where(x => x.PrimaryOperator != null && x.PrimaryOperator.Name == operatorName);
        var entities = await query.ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var states = entities.Select(x => (Passenger: x, State: BusinessRules.CalculatePassenger(x, today))).ToList();
        if (Enum.TryParse<PassengerOverallStatus>(overall, true, out var filterStatus)) states = states.Where(x => x.State.OverallStatus == filterStatus).ToList();
        var total = states.Count;
        var rooms = states.Select(x => x.Passenger.RoomReservation).Where(x => x is not null).DistinctBy(x => x!.Id).ToList();
        int CountRequirement(string key) => states.Count(x => x.State.Requirements.Single(r => r.Key == key).Status == VerificationStatus.Confirmed);
        int Percent(int value) => total == 0 ? 0 : (int)Math.Round(value * 100m / total);
        DashboardKpi Kpi(string key, string label, int value, string filter) => new(key, label, value, total, Percent(value), filter);

        var ready = states.Count(x => x.State.OverallStatus == PassengerOverallStatus.Ready);
        var pending = states.Count(x => x.State.OverallStatus == PassengerOverallStatus.Pending);
        var attention = states.Count(x => x.State.OverallStatus == PassengerOverallStatus.Attention);
        var kpis = new List<DashboardKpi>
        {
            Kpi("ready", "Pasajeros listos", ready, "overall=Ready"),
            Kpi("pending", "Pasajeros pendientes", pending, "overall=Pending"),
            Kpi("attention", "Pasajeros en atención", attention, "overall=Attention"),
            Kpi("roomsConfirmed", "Habitaciones confirmadas", CountRequirement("room"), "requirement=room&status=Confirmed"),
            Kpi("flights", "Tickets confirmados", CountRequirement("flight"), "requirement=flight&status=Confirmed"),
            Kpi("baggage", "Maletas confirmadas", CountRequirement("baggage"), "requirement=baggage&status=Confirmed"),
            Kpi("documentation", "Documentaciones verificadas", CountRequirement("documentation"), "requirement=documentation&status=Confirmed"),
            Kpi("passports", "Pasaportes completos", CountRequirement("passport"), "requirement=passport&status=Confirmed"),
            new("rooms", "Habitaciones", rooms.Count, rooms.Count, rooms.Count == 0 ? 0 : 100, "/rooms")
        };

        var labels = new Dictionary<string, string> { ["passport"]="Pasaporte", ["documentation"]="Documentación", ["room"]="Habitación", ["flight"]="Ticket de vuelo", ["baggage"]="Maleta de 23 kg" };
        var categories = labels.Select(pair =>
        {
            var requirements = states.Select(x => x.State.Requirements.Single(r => r.Key == pair.Key)).ToList();
            return new CategoryProgress(pair.Key, pair.Value, requirements.Count(x => x.Status == VerificationStatus.Confirmed),
                requirements.Count(x => x.Status == VerificationStatus.ToVerify), requirements.Count(x => x.Status == VerificationStatus.InProgress),
                requirements.Count(x => x.Status == VerificationStatus.NotIncluded), requirements.Count(x => x.Status == VerificationStatus.NotApplicable),
                Percent(requirements.Count(BusinessRules.IsResolved)));
        }).ToList();
        var operatorSummary = states.GroupBy(x => x.Passenger.PrimaryOperator?.Name ?? "Sin operadora").Select(group =>
        {
            var operatorRooms = group.Select(x => x.Passenger.RoomReservation).Where(x => x is not null).DistinctBy(x => x!.Id).ToList();
            return new OperatorSummary(group.Key, operatorRooms.Count, group.Count(), operatorRooms.Count(x => x!.Status == VerificationStatus.Confirmed),
                group.SelectMany(x => x.State.Alerts).Distinct().ToList());
        }).OrderBy(x => x.Name).ToList();

        var transferEntity = await db.TripTransferStatuses.AsNoTracking().SingleAsync(x => x.Trip.IsActive, ct);
        var transferUser = transferEntity.UpdatedByUserId.HasValue ? await users.FindByIdAsync(transferEntity.UpdatedByUserId.Value.ToString()) : null;
        var transfer = new TransferStatusResponse(transferEntity.IsConfirmed, transferEntity.ConfirmedAt, transferEntity.Notes,
            transferUser?.DisplayName, transferEntity.UpdatedAt, transferEntity.Version);
        var actions = new List<PriorityAction>
        {
            new("critical", "Pasajeros con alertas críticas", attention, "overall=Attention"),
            new("global", "Transfer grupal pendiente", transfer.IsConfirmed ? 0 : 1, "globalTransfer=pending"),
            new("warning", "Tickets de vuelo pendientes", total - CountRequirement("flight"), "requirement=flight"),
            new("warning", "Maletas de 23 kg pendientes", total - CountRequirement("baggage"), "requirement=baggage"),
            new("info", "Documentaciones pendientes", total - CountRequirement("documentation"), "requirement=documentation"),
            new("info", "Propiedades de hotel por completar", states.Count(x => x.Passenger.RoomReservation?.SpecificPropertyPending == true), "propertyPending=true")
        }.Where(x => x.Count > 0).ToList();

        var auditEntries = await db.AuditLogs.AsNoTracking().ToListAsync(ct);
        var activity = auditEntries.OrderByDescending(x => x.At).Take(12)
            .Select(x => new RecentActivity(x.Id, null, x.EntityName + " · " + x.Action, x.UserName, x.At, x.PreviousValue, x.NewValue)).ToList();
        var tripReadiness = BusinessRules.CalculateTrip(states.Select(x => x.State), transfer.IsConfirmed);
        return new(kpis, categories, new Dictionary<string, int> { ["ready"] = ready, ["pending"] = pending, ["attention"] = attention },
            operatorSummary, actions, activity, transfer, tripReadiness);
    }
}
