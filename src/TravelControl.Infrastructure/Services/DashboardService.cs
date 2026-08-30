using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TravelControl.Application.Contracts;
using TravelControl.Application.Services;
using TravelControl.Domain;
using TravelControl.Infrastructure.Identity;
using TravelControl.Infrastructure.Persistence;

namespace TravelControl.Infrastructure.Services;

public sealed class DashboardService(AppDbContext db, PassengerQueryService passengers, EvidenceResolver evidenceResolver, UserManager<AppUser> users)
{
    public async Task<DashboardResponse> GetAsync(string? operatorName, string? overall, CancellationToken ct)
    {
        var query = passengers.BaseQuery(asNoTracking: true);
        if (!string.IsNullOrWhiteSpace(operatorName)) query = query.Where(x => x.PrimaryOperator != null && x.PrimaryOperator.Name == operatorName);
        var entities = await query.ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var passengerIds = entities.Select(x => x.Id).ToArray();
        var evidence = await evidenceResolver.GetForPassengersAsync(passengerIds, ct);
        var states = entities.Select(x => (Passenger: x, State: BusinessRules.CalculatePassenger(x, today,
            evidence.GetValueOrDefault(x.Id) ?? new PassengerEvidenceState()))).ToList();
        if (Enum.TryParse<PassengerOverallStatus>(overall, true, out var filterStatus)) states = states.Where(x => x.State.OverallStatus == filterStatus).ToList();
        var total = states.Count;
        var rooms = string.IsNullOrWhiteSpace(operatorName) && string.IsNullOrWhiteSpace(overall)
            ? await db.RoomReservations.AsNoTracking().Include(x => x.Passengers).Where(x => x.Trip.IsActive).ToListAsync(ct)
            : states.Select(x => x.Passenger.RoomReservation).Where(x => x is not null).DistinctBy(x => x!.Id).Select(x => x!).ToList();
        var roomEvidence = await evidenceResolver.GetRoomEvidenceAsync(rooms.Select(x => x.Id), ct);
        bool RoomResolved(RoomReservation room) => room.Status == VerificationStatus.Confirmed
            && BusinessRules.RoomCanBeConfirmed(room, roomEvidence.Contains(room.Id), out _)
            || room.Status == VerificationStatus.NotApplicable
            && !string.IsNullOrWhiteSpace(room.CapacityOverrideReason ?? room.Notes);
        int CountRequirement(string key) => states.Count(x => BusinessRules.IsResolved(x.State.Requirements.Single(r => r.Key == key)));
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
            Kpi("accommodationPassengers", "Pasajeros con alojamiento resuelto", CountRequirement("room"), "requirement=room"),
            Kpi("flights", "Tickets resueltos", CountRequirement("flight"), "requirement=flight"),
            Kpi("baggage", "Maletas resueltas", CountRequirement("baggage"), "requirement=baggage"),
            Kpi("documentation", "Documentaciones resueltas", CountRequirement("documentation"), "requirement=documentation"),
            Kpi("passports", "Pasaportes completos", CountRequirement("passport"), "requirement=passport"),
            new("roomsConfirmed", "Habitaciones confirmadas", rooms.Count(x => RoomResolved(x!)), rooms.Count,
                rooms.Count == 0 ? 0 : PercentOf(rooms.Count(x => RoomResolved(x!)), rooms.Count), "/gestion/habitaciones")
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
            return new OperatorSummary(group.Key, operatorRooms.Count, group.Count(), operatorRooms.Count(x => RoomResolved(x!)),
                group.SelectMany(x => x.State.Alerts).Distinct().ToList());
        }).OrderBy(x => x.Name).ToList();

        var transferEntity = await db.TripTransferStatuses.AsNoTracking().SingleAsync(x => x.Trip.IsActive, ct);
        var transferUser = transferEntity.UpdatedByUserId.HasValue ? await users.FindByIdAsync(transferEntity.UpdatedByUserId.Value.ToString()) : null;
        var transfer = new TransferStatusResponse(transferEntity.IsConfirmed, transferEntity.ConfirmedAt, transferEntity.Notes,
            transferUser?.DisplayName, transferEntity.UpdatedAt, transferEntity.Version);
        var actions = new List<PriorityAction>
        {
            new("critical", "Pasajeros con alertas críticas", attention, "overall=Attention"),
            new("critical", "Transfer grupal pendiente", transfer.IsConfirmed ? 0 : 1, "globalTransfer=pending"),
            new("warning", "Tickets de vuelo pendientes", total - CountRequirement("flight"), "requirement=flight"),
            new("warning", "Maletas de 23 kg pendientes", total - CountRequirement("baggage"), "requirement=baggage"),
            new("info", "Documentaciones pendientes", total - CountRequirement("documentation"), "requirement=documentation"),
            new("info", "Pasajeros con alojamiento pendiente", total - CountRequirement("room"), "requirement=room"),
            new("info", "Reservas de habitación pendientes", rooms.Count(x => !RoomResolved(x!)), "/gestion/habitaciones"),
            new("info", "Propiedades de hotel por completar", rooms.Count(x => x.SpecificPropertyPending), "propertyPending=true")
        }.Where(x => x.Count > 0).ToList();

        var auditEntries = await db.AuditLogs.AsNoTracking().ToListAsync(ct);
        var activity = auditEntries.OrderByDescending(x => x.At).Take(12)
            .Select(x => new RecentActivity(x.Id, null, x.EntityName + " · " + x.Action, x.UserName, x.At, x.PreviousValue, x.NewValue)).ToList();
        var tripReadiness = BusinessRules.CalculateTrip(states.Select(x => x.State), transfer.IsConfirmed);
        return new(kpis, categories, new Dictionary<string, int> { ["ready"] = ready, ["pending"] = pending, ["attention"] = attention },
            operatorSummary, actions, activity, transfer, tripReadiness);
    }

    private static int PercentOf(int value, int total) => total == 0 ? 0 : (int)Math.Round(value * 100m / total);
}
