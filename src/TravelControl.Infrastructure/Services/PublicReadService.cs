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
    TripReadinessService readiness,
    IOptions<PublicReadOptions> options)
{
    private static readonly string[] RequirementKeys = ["passport", "documentation", "room", "flight", "baggage"];
    private readonly PublicReadOptions _options = options.Value;
    public bool Enabled => _options.Enabled;

    public async Task<PagedResult<PublicPassengerDto>> GetPassengersAsync(string? search, string? operatorName,
        string? overall, string? requirement, string? status, int page, int pageSize, CancellationToken ct)
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
        var transfer = await db.TripTransferStatuses.AsNoTracking().Where(x => x.Trip.IsActive).Select(x => x.IsConfirmed).SingleAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var mapped = entities.Select(x => (Entity: x, State: BusinessRules.CalculatePassenger(x, today,
                evidence.GetValueOrDefault(x.Id) ?? new PassengerEvidenceState())))
            .Where(x => !Enum.TryParse<PassengerOverallStatus>(overall, true, out var value) || x.State.OverallStatus == value)
            .Where(x => FilterRequirement(x.State, requirement, status)).Select(x => Map(x.Entity, x.State, transfer)).ToList();
        return new(mapped.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, mapped.Count);
    }

    public async Task<PublicPassengerDto?> GetPassengerAsync(Guid id, CancellationToken ct)
    {
        var entity = await passengers.BaseQuery(asNoTracking: true).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return null;
        var evidence = await evidenceResolver.GetForPassengersAsync([id], ct);
        var transfer = await db.TripTransferStatuses.AsNoTracking().Where(x => x.Trip.IsActive).Select(x => x.IsConfirmed).SingleAsync(ct);
        return Map(entity, BusinessRules.CalculatePassenger(entity, DateOnly.FromDateTime(DateTime.UtcNow),
            evidence.GetValueOrDefault(id) ?? new PassengerEvidenceState()), transfer);
    }

    public async Task<PublicDashboardDto> GetDashboardAsync(CancellationToken ct)
    {
        var snapshot = await readiness.GetAsync(ct);
        var total = snapshot.TotalPassengers;
        int Percent(int value) => total == 0 ? 0 : (int)Math.Round(value * 100m / total);
        var labels = new Dictionary<string, string>
        {
            ["passport"] = "Pasaporte", ["documentation"] = "Documentación", ["room"] = "Habitación",
            ["flight"] = "Ticket de vuelo", ["baggage"] = "Maleta de 23 kg"
        };
        var categories = labels.Select(pair =>
        {
            var value = snapshot.Requirements[pair.Key];
            return new PublicCategoryProgress(pair.Key, pair.Value, value.Confirmed, value.Pending, value.InProgress,
                value.NotIncluded, value.NotApplicable, Percent(value.Resolved));
        }).ToArray();
        var operators = snapshot.Passengers.GroupBy(x => x.Passenger.PrimaryOperator?.Name ?? "Sin operadora").Select(group =>
        {
            var operatorRooms = group.Select(x => x.Passenger.RoomReservation).Where(x => x is not null).DistinctBy(x => x!.Id).ToArray();
            return new PublicOperatorSummary(group.Key, operatorRooms.Length, group.Count(),
                operatorRooms.Count(room => snapshot.Passengers.Any(p => p.Passenger.RoomReservationId == room!.Id
                    && BusinessRules.IsResolved(Requirement(p.State, "room")))));
        }).OrderBy(x => x.Name).ToArray();
        var missing = new PublicMissingCounts(total - snapshot.Requirements["flight"].Resolved,
            total - snapshot.Requirements["baggage"].Resolved, total - snapshot.Requirements["documentation"].Resolved,
            total - snapshot.Requirements["passport"].Resolved, total - snapshot.Requirements["room"].Resolved,
            snapshot.RoomsPending, snapshot.SpecificPropertiesPending, !snapshot.Transfer.IsConfirmed);
        var alerts = snapshot.Blockers.Select(x => x.Key switch
        {
            "transfer" => "Transfer grupal pendiente",
            "properties" => "Hay propiedades de hotel pendientes",
            "rooms" => "Hay reservas de habitación pendientes",
            "passenger-attention" => "Hay inconsistencias que requieren atención",
            _ => "El estado general requiere atención"
        }).Distinct().ToArray();
        var kpis = new[]
        {
            Kpi("ready", "Pasajeros listos", snapshot.ReadyPassengers, total),
            Kpi("pending", "Pasajeros pendientes", snapshot.PendingPassengers, total),
            Kpi("attention", "Pasajeros en atención", snapshot.AttentionPassengers, total),
            Kpi("accommodationPassengers", "Pasajeros con alojamiento resuelto", snapshot.AccommodationPassengersResolved, total),
            Kpi("roomsConfirmed", "Habitaciones confirmadas", snapshot.RoomsConfirmed, snapshot.Rooms.Count),
            Kpi("flights", "Tickets resueltos", snapshot.Requirements["flight"].Resolved, total),
            Kpi("baggage", "Maletas resueltas", snapshot.Requirements["baggage"].Resolved, total),
            Kpi("documentation", "Documentaciones resueltas", snapshot.Requirements["documentation"].Resolved, total),
            Kpi("passports", "Pasaportes completos", snapshot.Requirements["passport"].Resolved, total)
        };
        return new(snapshot.Trip.Name, snapshot.Trip.Destination, total, snapshot.ReadyPassengers, snapshot.PendingPassengers,
            snapshot.AttentionPassengers, snapshot.ProgressPercent, snapshot.OverallStatus, snapshot.Transfer.IsConfirmed, kpis, categories,
            operators, missing, alerts, snapshot.UpdatedAt);
    }

    private static PublicDashboardKpi Kpi(string key, string label, int value, int total) =>
        new(key, label, value, total, total == 0 ? 0 : (int)Math.Round(value * 100m / total));

    private PublicPassengerDto Map(Passenger passenger, PassengerComputedState state, bool transferConfirmed)
    {
        var requirements = state.Requirements.Select(x => new PublicRequirementDto(x.Key, x.Label, x.Status)).ToArray();
        var missing = state.Requirements.Where(x => !BusinessRules.IsResolved(x)).Select(x => x.Label).ToArray();
        return new(passenger.Id, PublicName(passenger), passenger.PrimaryOperator?.Name, passenger.RoomReservation?.InternalCode,
            passenger.RoomReservation?.Hotel, passenger.RoomReservation?.RoomType, passenger.RoomReservation?.CheckIn,
            passenger.RoomReservation?.CheckOut, state.OverallStatus, state.ProgressPercent, requirements, missing,
            SanitizeAlerts(state.Alerts), transferConfirmed);
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
        return Enum.TryParse<VerificationStatus>(status, true, out var value) ? requirement.Status == value : !BusinessRules.IsResolved(requirement);
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
