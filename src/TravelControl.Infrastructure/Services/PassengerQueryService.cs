using Microsoft.EntityFrameworkCore;
using TravelControl.Application.Contracts;
using TravelControl.Application.Services;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;

namespace TravelControl.Infrastructure.Services;

public sealed class PassengerQueryService(AppDbContext db, EvidenceResolver evidenceResolver)
{
    public IQueryable<Passenger> BaseQuery(bool asNoTracking = false)
    {
        var query = db.Passengers.AsSplitQuery()
        .Include(x => x.Trip).Include(x => x.PrimaryOperator)
        .Include(x => x.RoomReservation).ThenInclude(x => x!.Passengers)
        .Include(x => x.PassengerFlights).ThenInclude(x => x.FlightBooking).ThenInclude(x => x.Segments)
        .Include(x => x.BaggageEntitlements).Include(x => x.FollowUps);
        return asNoTracking ? query.AsNoTrackingWithIdentityResolution() : query;
    }

    public static PassengerListItem Map(Passenger passenger, DateOnly today, bool hasAirTicketEvidence = false)
    {
        var state = BusinessRules.CalculatePassenger(passenger, today, hasAirTicketEvidence);
        return new(passenger.Id, passenger.FullName, MaskPassport(passenger.PassportNumber), state.PassportStatus,
            passenger.PrimaryOperator?.Name, passenger.RoomReservation?.InternalCode, passenger.RoomReservation?.Hotel, passenger.RoomReservation?.RoomType,
            passenger.RoomReservation?.CheckIn, passenger.RoomReservation?.CheckOut, passenger.RoomReservation?.Nights,
            passenger.DocumentationStatus, state.OverallStatus, state.ProgressPercent, state.Requirements, state.Alerts,
            passenger.NextAction, passenger.NextActionDueDate, passenger.UpdatedAt, passenger.Version);
    }

    public static PassengerListItem Map(Passenger passenger, DateOnly today, PassengerEvidenceState evidence)
    {
        var state = BusinessRules.CalculatePassenger(passenger, today, evidence);
        return new(passenger.Id, passenger.FullName, MaskPassport(passenger.PassportNumber), state.PassportStatus,
            passenger.PrimaryOperator?.Name, passenger.RoomReservation?.InternalCode, passenger.RoomReservation?.Hotel, passenger.RoomReservation?.RoomType,
            passenger.RoomReservation?.CheckIn, passenger.RoomReservation?.CheckOut, passenger.RoomReservation?.Nights,
            passenger.DocumentationStatus, state.OverallStatus, state.ProgressPercent, state.Requirements, state.Alerts,
            passenger.NextAction, passenger.NextActionDueDate, passenger.UpdatedAt, passenger.Version);
    }

    public static string MaskPassport(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Sin cargar";
        var visible = value.Length <= 3 ? value[^1..] : value[^3..];
        return new string('\u2022', Math.Max(4, value.Length - visible.Length)) + visible;
    }

    public Task<List<Guid>> AttachmentsWithAirTicketEvidenceAsync(IEnumerable<Guid> passengerIds, CancellationToken ct)
    {
        return AirTicketEvidenceAsync(passengerIds, ct);
    }

    public Task<IReadOnlyDictionary<Guid, PassengerEvidenceState>> EvidenceAsync(IEnumerable<Guid> passengerIds, CancellationToken ct) =>
        evidenceResolver.GetForPassengersAsync(passengerIds, ct);

    private async Task<List<Guid>> AirTicketEvidenceAsync(IEnumerable<Guid> passengerIds, CancellationToken ct)
    {
        var evidence = await evidenceResolver.GetForPassengersAsync(passengerIds, ct);
        return evidence.Where(x => x.Value.HasAirTicketEvidence).Select(x => x.Key).ToList();
    }
}
