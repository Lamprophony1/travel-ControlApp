using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TravelControl.Application.Contracts;
using TravelControl.Application.Services;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;

namespace TravelControl.Infrastructure.Services;

public sealed class PublicReadOptions
{
    public bool Enabled { get; init; } = true;
    public string NameMode { get; init; } = "Full";
}

public sealed class PublicReadService(
    AppDbContext db,
    PassengerQueryService passengers,
    EvidenceResolver evidenceResolver,
    IOptions<PublicReadOptions> options)
{
    private static readonly string[] RequirementKeys = ["passport", "documentation", "room", "flight", "baggage"];
    private readonly PublicReadOptions _options = options.Value;

    public bool Enabled => _options.Enabled;

    public async Task<PagedResult<PublicPassengerDto>> GetPassengersAsync(
        string? search,
        string? operatorName,
        string? overall,
        string? requirement,
        string? status,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize == 0 ? 25 : pageSize, 1, 50);
        var query = passengers.BaseQuery(asNoTracking: true);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = TextNormalizer.Normalize(search);
            var raw = search.Trim();
            query = query.Where(x => x.NormalizedName.Contains(normalized)
                || (x.RoomReservation != null && x.RoomReservation.InternalCode.Contains(raw)));
        }
        if (!string.IsNullOrWhiteSpace(operatorName))
            query = query.Where(x => x.PrimaryOperator != null && x.PrimaryOperator.Name == operatorName);

        var entities = await query.OrderBy(x => x.FullName).ToListAsync(ct);
        var evidence = await evidenceResolver.GetForPassengersAsync(entities.Select(x => x.Id), ct);
        var transfer = await db.TripTransferStatuses.AsNoTracking().Where(x => x.Trip.IsActive)
            .Select(x => x.IsConfirmed).SingleAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var mapped = entities.Select(x => (Entity: x, State: BusinessRules.CalculatePassenger(x, today,
                evidence.GetValueOrDefault(x.Id) ?? new PassengerEvidenceState())))
            .Where(x => !Enum.TryParse<PassengerOverallStatus>(overall, true, out var value) || x.State.OverallStatus == value)
            .Where(x => FilterRequirement(x.State, requirement, status))
            .Select(x => Map(x.Entity, x.State, transfer))
            .ToList();
        return new(mapped.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, mapped.Count);
    }

    public async Task<PublicPassengerDto?> GetPassengerAsync(Guid id, CancellationToken ct)
    {
        var entity = await passengers.BaseQuery(asNoTracking: true).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return null;
        var evidence = await evidenceResolver.GetForPassengersAsync([id], ct);
        var transfer = await db.TripTransferStatuses.AsNoTracking().Where(x => x.Trip.IsActive)
            .Select(x => x.IsConfirmed).SingleAsync(ct);
        return Map(entity, BusinessRules.CalculatePassenger(entity, DateOnly.FromDateTime(DateTime.UtcNow),
            evidence.GetValueOrDefault(id) ?? new PassengerEvidenceState()), transfer);
    }

    public async Task<PublicDashboardDto> GetDashboardAsync(CancellationToken ct)
    {
        var entities = await passengers.BaseQuery(asNoTracking: true).OrderBy(x => x.FullName).ToListAsync(ct);
        var evidence = await evidenceResolver.GetForPassengersAsync(entities.Select(x => x.Id), ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var states = entities.Select(x => (Passenger: x, State: BusinessRules.CalculatePassenger(x, today,
            evidence.GetValueOrDefault(x.Id) ?? new PassengerEvidenceState()))).ToList();
        var trip = await db.Trips.AsNoTracking().SingleAsync(x => x.IsActive, ct);
        var transfer = await db.TripTransferStatuses.AsNoTracking().SingleAsync(x => x.TripId == trip.Id, ct);
        var total = states.Count;
        int CountResolved(string key) => states.Count(x => BusinessRules.IsResolved(Requirement(x.State, key)));
        int Percent(int value) => total == 0 ? 0 : (int)Math.Round(value * 100m / total);
        var ready = states.Count(x => x.State.OverallStatus == PassengerOverallStatus.Ready);
        var pending = states.Count(x => x.State.OverallStatus == PassengerOverallStatus.Pending);
        var attention = states.Count(x => x.State.OverallStatus == PassengerOverallStatus.Attention);
        var rooms = await db.RoomReservations.AsNoTracking().Include(x => x.Passengers)
            .Where(x => x.TripId == trip.Id).OrderBy(x => x.InternalCode).ToArrayAsync(ct);
        var roomEvidence = await evidenceResolver.GetRoomEvidenceAsync(rooms.Select(x => x.Id), ct);
        bool RoomResolved(RoomReservation room) => room.Status == VerificationStatus.Confirmed
            && BusinessRules.RoomCanBeConfirmed(room, roomEvidence.Contains(room.Id), out _)
            || room.Status == VerificationStatus.NotApplicable
            && !string.IsNullOrWhiteSpace(room.CapacityOverrideReason ?? room.Notes);
        var labels = new Dictionary<string, string>
        {
            ["passport"] = "Pasaporte", ["documentation"] = "Documentación", ["room"] = "Habitación",
            ["flight"] = "Ticket de vuelo", ["baggage"] = "Maleta de 23 kg"
        };
        var categories = labels.Select(pair =>
        {
            var values = states.Select(x => Requirement(x.State, pair.Key)).ToArray();
            return new PublicCategoryProgress(pair.Key, pair.Value,
                values.Count(x => x.Status == VerificationStatus.Confirmed),
                values.Count(x => x.Status == VerificationStatus.ToVerify),
                values.Count(x => x.Status == VerificationStatus.InProgress),
                values.Count(x => x.Status == VerificationStatus.NotIncluded),
                values.Count(x => x.Status == VerificationStatus.NotApplicable),
                Percent(values.Count(BusinessRules.IsResolved)));
        }).ToArray();
        var operators = states.GroupBy(x => x.Passenger.PrimaryOperator?.Name ?? "Sin operadora")
            .Select(group =>
            {
                var operatorRooms = group.Select(x => x.Passenger.RoomReservation).Where(x => x is not null).DistinctBy(x => x!.Id).ToArray();
                return new PublicOperatorSummary(group.Key, operatorRooms.Length, group.Count(),
                    operatorRooms.Count(room => RoomResolved(room!)));
            }).OrderBy(x => x.Name).ToArray();
        var properties = rooms.Count(x => x!.SpecificPropertyPending);
        var missing = new PublicMissingCounts(
            total - CountResolved("flight"), total - CountResolved("baggage"), total - CountResolved("documentation"),
            total - CountResolved("passport"), total - CountResolved("room"), rooms.Count(x => !RoomResolved(x)), properties, !transfer.IsConfirmed);
        var alerts = new List<string>();
        if (!transfer.IsConfirmed) alerts.Add("Transfer grupal pendiente");
        if (properties > 0) alerts.Add("Hay propiedades de hotel pendientes");
        if (attention > 0) alerts.Add("Hay inconsistencias que requieren atención");
        var tripState = BusinessRules.CalculateTrip(states.Select(x => x.State), transfer.IsConfirmed, alerts);
        var kpis = new[]
        {
            Kpi("ready", "Pasajeros listos", ready, total), Kpi("pending", "Pasajeros pendientes", pending, total),
            Kpi("attention", "Pasajeros en atención", attention, total),
            Kpi("accommodationPassengers", "Pasajeros con alojamiento resuelto", CountResolved("room"), total),
            Kpi("roomsConfirmed", "Habitaciones confirmadas", rooms.Count(RoomResolved), rooms.Length),
            Kpi("flights", "Tickets resueltos", CountResolved("flight"), total), Kpi("baggage", "Maletas resueltas", CountResolved("baggage"), total),
            Kpi("documentation", "Documentaciones resueltas", CountResolved("documentation"), total),
            Kpi("passports", "Pasaportes completos", CountResolved("passport"), total)
        };
        var updated = await evidenceResolver.GetOperationalUpdatedAtAsync(trip.UpdatedAt, ct);
        return new(trip.Name, trip.Destination, total, ready, pending, attention, tripState.ProgressPercent,
            transfer.IsConfirmed, kpis, categories, operators, missing, alerts, updated);
    }

    private PublicDashboardKpi Kpi(string key, string label, int value, int total) =>
        new(key, label, value, total, total == 0 ? 0 : (int)Math.Round(value * 100m / total));

    private PublicPassengerDto Map(Passenger passenger, PassengerComputedState state, bool transferConfirmed)
    {
        var requirements = state.Requirements.Select(x => new PublicRequirementDto(x.Key, x.Label, x.Status)).ToArray();
        var missing = state.Requirements.Where(x => !BusinessRules.IsResolved(x)).Select(x => x.Label).ToArray();
        return new(passenger.Id, PublicName(passenger), passenger.PrimaryOperator?.Name,
            passenger.RoomReservation?.InternalCode, passenger.RoomReservation?.Hotel, passenger.RoomReservation?.RoomType,
            passenger.RoomReservation?.CheckIn, passenger.RoomReservation?.CheckOut, state.OverallStatus, state.ProgressPercent,
            requirements, missing, SanitizeAlerts(state.Alerts), transferConfirmed);
    }

    private string PublicName(Passenger passenger) => _options.NameMode.Trim() switch
    {
        "Initials" => Initials(passenger.FullName),
        "FirstNameLastInitial" => FirstNameLastInitial(passenger.FullName),
        _ => passenger.FullName
    };

    private static bool FilterRequirement(PassengerComputedState state, string? key, string? status)
    {
        if (string.IsNullOrWhiteSpace(key) || !RequirementKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) return true;
        var requirement = state.Requirements.First(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        return Enum.TryParse<VerificationStatus>(status, true, out var value)
            ? requirement.Status == value : !BusinessRules.IsResolved(requirement);
    }

    private static RequirementState Requirement(PassengerComputedState state, string key) => state.Requirements.Single(x => x.Key == key);
    private static IReadOnlyList<string> SanitizeAlerts(IEnumerable<string> alerts) => alerts.Select(x => x switch
    {
        BusinessRules.TopTravelPropertyAlert => "Propiedad específica del hotel pendiente",
        BusinessRules.StaleDocumentationAlert => BusinessRules.StaleDocumentationAlert,
        "Pasajero sin habitación" => "Habitación pendiente",
        "Pasaporte vencido antes del regreso" => "Estado de pasaporte requiere atención",
        "Fechas de alojamiento inconsistentes" => "Fechas de alojamiento inconsistentes",
        "Ocupación incompatible con la capacidad" => "Ocupación de habitación requiere atención",
        "Existe un requisito no incluido" => "Existe un requisito no incluido",
        _ => "Existe una inconsistencia que requiere atención"
    }).Distinct().ToArray();
    private static string Initials(string fullName) => string.Join(" ", fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => $"{char.ToUpperInvariant(x[0])}."));
    private static string FirstNameLastInitial(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 ? fullName : $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.";
    }
}
