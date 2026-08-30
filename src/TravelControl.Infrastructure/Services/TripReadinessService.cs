using Microsoft.EntityFrameworkCore;
using TravelControl.Application.Services;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;

namespace TravelControl.Infrastructure.Services;

public sealed record PassengerReadiness(Passenger Passenger, PassengerComputedState State);
public sealed record RequirementReadiness(int Confirmed, int Pending, int InProgress, int NotIncluded, int NotApplicable, int Resolved);
public sealed record GlobalReadinessBlocker(string Key, string Message, string Severity);
public sealed record TripReadinessSnapshot(
    Trip Trip,
    TripTransferStatus Transfer,
    IReadOnlyList<PassengerReadiness> Passengers,
    IReadOnlyList<RoomReservation> Rooms,
    int TotalPassengers,
    int ReadyPassengers,
    int PendingPassengers,
    int AttentionPassengers,
    int BaseProgressPercent,
    int ProgressPercent,
    int AccommodationPassengersResolved,
    int RoomsConfirmed,
    int RoomsPending,
    int SpecificPropertiesPending,
    IReadOnlyDictionary<string, RequirementReadiness> Requirements,
    IReadOnlyList<GlobalReadinessBlocker> Blockers,
    TripOverallStatus OverallStatus,
    DateTimeOffset UpdatedAt);

public sealed class TripReadinessService(
    AppDbContext db,
    PassengerQueryService passengerQueries,
    EvidenceResolver evidenceResolver)
{
    private static readonly string[] RequirementKeys = ["passport", "documentation", "room", "flight", "baggage"];

    public async Task<TripReadinessSnapshot> GetAsync(CancellationToken ct)
    {
        var trip = await db.Trips.AsNoTracking().SingleAsync(x => x.IsActive, ct);
        var transfer = await db.TripTransferStatuses.AsNoTracking().SingleAsync(x => x.TripId == trip.Id, ct);
        var passengers = await passengerQueries.BaseQuery(asNoTracking: true).OrderBy(x => x.FullName).ToListAsync(ct);
        var rooms = await db.RoomReservations.AsNoTracking().Include(x => x.Operator).Include(x => x.Passengers)
            .Where(x => x.TripId == trip.Id).OrderBy(x => x.InternalCode).ToListAsync(ct);
        var evidence = await evidenceResolver.GetForPassengersAsync(passengers.Select(x => x.Id), ct);
        var roomEvidence = await evidenceResolver.GetRoomEvidenceAsync(rooms.Select(x => x.Id), ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var states = passengers.Select(x => new PassengerReadiness(x,
            BusinessRules.CalculatePassenger(x, today, evidence.GetValueOrDefault(x.Id) ?? new PassengerEvidenceState()))).ToArray();
        bool RoomResolved(RoomReservation room) => room.Status == VerificationStatus.Confirmed
                && BusinessRules.RoomCanBeConfirmed(room, roomEvidence.Contains(room.Id), out _)
            || room.Status == VerificationStatus.NotApplicable
                && !string.IsNullOrWhiteSpace(room.CapacityOverrideReason ?? room.Notes);
        var roomsConfirmed = rooms.Count(RoomResolved);
        var propertiesPending = rooms.Count(x => x.SpecificPropertyPending);
        var attention = states.Count(x => x.State.OverallStatus == PassengerOverallStatus.Attention);
        var blockers = new List<GlobalReadinessBlocker>();
        if (states.Length == 0) blockers.Add(new("passengers-missing", "No hay pasajeros para evaluar.", "critical"));
        if (rooms.Count == 0) blockers.Add(new("rooms-missing", "No hay habitaciones para evaluar.", "critical"));
        if (!transfer.IsConfirmed) blockers.Add(new("transfer", "Transfer grupal pendiente.", "pending"));
        if (roomsConfirmed < rooms.Count) blockers.Add(new("rooms", "Hay reservas de habitación pendientes.", "pending"));
        if (propertiesPending > 0) blockers.Add(new("properties", "Hay propiedades específicas pendientes.", "critical"));
        if (attention > 0) blockers.Add(new("passenger-attention", "Hay pasajeros que requieren atención.", "critical"));

        var allPassengersReady = states.Length > 0 && states.All(x => x.State.OverallStatus == PassengerOverallStatus.Ready);
        var isReady = allPassengersReady && transfer.IsConfirmed && rooms.Count > 0 && roomsConfirmed == rooms.Count
            && propertiesPending == 0 && blockers.Count == 0;
        var overall = isReady ? TripOverallStatus.Ready
            : blockers.Any(x => x.Severity == "critical") ? TripOverallStatus.Attention : TripOverallStatus.Pending;
        var passengerAverage = states.Length == 0 ? 0 : states.Average(x => x.State.ProgressPercent);
        var baseProgress = Math.Clamp((int)Math.Round(passengerAverage * 0.9d) + (transfer.IsConfirmed ? 10 : 0), 0, 100);
        var visibleProgress = overall != TripOverallStatus.Ready && baseProgress == 100 ? 99 : baseProgress;
        var requirements = RequirementKeys.ToDictionary(key => key, key =>
        {
            var values = states.Select(x => x.State.Requirements.Single(r => r.Key == key)).ToArray();
            return new RequirementReadiness(values.Count(x => x.Status == VerificationStatus.Confirmed),
                values.Count(x => x.Status == VerificationStatus.ToVerify), values.Count(x => x.Status == VerificationStatus.InProgress),
                values.Count(x => x.Status == VerificationStatus.NotIncluded), values.Count(x => x.Status == VerificationStatus.NotApplicable),
                values.Count(BusinessRules.IsResolved));
        });
        var updated = await evidenceResolver.GetOperationalUpdatedAtAsync(trip.UpdatedAt, ct);
        return new(trip, transfer, states, rooms, states.Length,
            states.Count(x => x.State.OverallStatus == PassengerOverallStatus.Ready),
            states.Count(x => x.State.OverallStatus == PassengerOverallStatus.Pending), attention,
            baseProgress, visibleProgress, requirements["room"].Resolved, roomsConfirmed, rooms.Count - roomsConfirmed,
            propertiesPending, requirements, blockers, overall, updated);
    }
}
