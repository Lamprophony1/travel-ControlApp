using TravelControl.Domain;

namespace TravelControl.Application.Services;

public sealed record RequirementState(string Key, VerificationStatus Status, string Label, string? Reason = null);
public sealed record PassengerComputedState(
    PassportStatus PassportStatus,
    PassengerOverallStatus OverallStatus,
    int ProgressPercent,
    IReadOnlyList<RequirementState> Requirements,
    IReadOnlyList<string> Alerts);
public sealed record TripComputedState(
    TripOverallStatus OverallStatus,
    int ProgressPercent,
    bool AllPassengersReady,
    bool TransferConfirmed,
    IReadOnlyList<string> CriticalAlerts);

public static class BusinessRules
{
    public const string TopTravelPropertyAlert = "Propiedad específica del hotel pendiente de confirmar con Top Travel";

    public static PassportStatus CalculatePassport(Passenger passenger, DateOnly returnDate, int warningDays, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(passenger.PassportNumber) || string.IsNullOrWhiteSpace(passenger.Nationality)
            || passenger.BirthDate is null || passenger.PassportExpiry is null) return PassportStatus.Incomplete;
        if (passenger.PassportExpiry < returnDate) return PassportStatus.Expired;
        return passenger.PassportExpiry.Value.DayNumber - today.DayNumber <= warningDays
            ? PassportStatus.ExpiringSoon : PassportStatus.Valid;
    }

    public static bool RoomCanBeConfirmed(RoomReservation? room, out string[] missing)
    {
        var values = new List<string>();
        if (room is null) values.Add("habitación asignada");
        else
        {
            if (room.OperatorId == Guid.Empty) values.Add("operadora");
            if (room.CheckIn is null) values.Add("check-in");
            if (room.CheckOut is null) values.Add("check-out");
            if (string.IsNullOrWhiteSpace(room.RoomType)) values.Add("tipo de habitación");
            if (string.IsNullOrWhiteSpace(room.SourceReference)) values.Add("fuente o referencia");
            if (room.CheckIn >= room.CheckOut) values.Add("fechas válidas");
            if (!room.CapacityOverride && room.Passengers.Count > room.ExpectedCapacity) values.Add("capacidad compatible");
        }
        missing = [.. values];
        return values.Count == 0;
    }

    public static bool FlightCanBeConfirmed(FlightBooking booking, PassengerFlight passengerFlight, out string[] missing)
    {
        var values = new List<string>();
        if (string.IsNullOrWhiteSpace(booking.Airline)) values.Add("aerolínea");
        if (string.IsNullOrWhiteSpace(booking.Pnr)) values.Add("PNR");
        if (string.IsNullOrWhiteSpace(passengerFlight.ElectronicTicketNumber)) values.Add("ticket electrónico");
        if (!booking.Segments.Any(x => x.Type == SegmentType.Outbound)) values.Add("segmento de ida");
        if (!booking.Segments.Any(x => x.Type == SegmentType.Return)) values.Add("segmento de regreso");
        missing = [.. values];
        return values.Count == 0;
    }

    public static bool BaggageCanBeConfirmed(BaggageEntitlement baggage, bool hasTicket, out string[] missing)
    {
        var values = new List<string>();
        if (!hasTicket) values.Add("ticket confirmado o asociado");
        if (baggage.CheckedBagCount < 1) values.Add("al menos una maleta");
        if (baggage.WeightPerBagKg < 23) values.Add("peso mínimo de 23 kg");
        if (!(baggage.AppliesOutbound && baggage.AppliesReturn) && string.IsNullOrWhiteSpace(baggage.ExceptionReason))
            values.Add("ida y regreso o excepción documentada");
        missing = [.. values];
        return values.Count == 0;
    }

    public static PassengerComputedState CalculatePassenger(Passenger passenger, DateOnly today)
    {
        var returnDate = passenger.RoomReservation?.CheckOut ?? passenger.Trip.EndDate;
        var passport = CalculatePassport(passenger, returnDate, passenger.Trip.PassportWarningDays, today);
        var passportRequirement = passport == PassportStatus.Valid
            ? VerificationStatus.Confirmed
            : passport == PassportStatus.Expired ? VerificationStatus.NotIncluded : VerificationStatus.ToVerify;
        var requirements = new[]
        {
            new RequirementState("passport", passportRequirement, "Pasaporte", passport.ToString()),
            new RequirementState("documentation", passenger.DocumentationStatus, "Documentación"),
            new RequirementState("room", passenger.RoomReservation?.Status ?? VerificationStatus.ToVerify, "Habitación"),
            new RequirementState("flight", Aggregate(passenger.PassengerFlights.Select(x => x.TicketStatus)), "Ticket de vuelo"),
            new RequirementState("baggage", Aggregate(passenger.BaggageEntitlements.Select(x => x.Status)), "Maleta de 23 kg")
        };

        var alerts = new List<string>();
        if (passenger.RoomReservation is null) alerts.Add("Pasajero sin habitación");
        if (passenger.RoomReservation?.SpecificPropertyPending == true) alerts.Add(TopTravelPropertyAlert);
        if (passport == PassportStatus.Expired) alerts.Add("Pasaporte vencido antes del regreso");
        if (passenger.RoomReservation?.CheckIn >= passenger.RoomReservation?.CheckOut) alerts.Add("Fechas de alojamiento inconsistentes");
        if (passenger.RoomReservation is { CapacityOverride: false } room && room.Passengers.Count > room.ExpectedCapacity)
            alerts.Add("Ocupación incompatible con la capacidad");
        if (requirements.Any(x => x.Status == VerificationStatus.NotIncluded)) alerts.Add("Existe un requisito no incluido");

        var resolved = requirements.Count(IsResolved);
        var progress = resolved * 20;
        var critical = alerts.Any(x => x != TopTravelPropertyAlert);
        var overall = critical ? PassengerOverallStatus.Attention
            : resolved == requirements.Length ? PassengerOverallStatus.Ready : PassengerOverallStatus.Pending;
        return new(passport, overall, progress, requirements, alerts);
    }

    public static TripComputedState CalculateTrip(IEnumerable<PassengerComputedState> passengers, bool transferConfirmed, IEnumerable<string>? globalCriticalAlerts = null)
    {
        var people = passengers.ToArray();
        var alerts = globalCriticalAlerts?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray() ?? [];
        var allPassengersReady = people.Length > 0 && people.All(x => x.OverallStatus == PassengerOverallStatus.Ready);
        var overall = alerts.Length > 0 ? TripOverallStatus.Attention
            : allPassengersReady && transferConfirmed ? TripOverallStatus.Ready : TripOverallStatus.Pending;
        var readinessParts = people.Length == 0 ? 0 : (int)Math.Round(people.Average(x => x.ProgressPercent) * 0.9d);
        var progress = Math.Clamp(readinessParts + (transferConfirmed ? 10 : 0), 0, 100);
        return new(overall, progress, allPassengersReady, transferConfirmed, alerts);
    }

    public static bool IsResolved(RequirementState requirement) => IsResolved(requirement.Status)
        && (requirement.Status != VerificationStatus.NotApplicable || !string.IsNullOrWhiteSpace(requirement.Reason));
    public static bool IsResolved(VerificationStatus status) => status is VerificationStatus.Confirmed or VerificationStatus.NotApplicable;

    private static VerificationStatus Aggregate(IEnumerable<VerificationStatus> values)
    {
        var list = values.ToArray();
        if (list.Length == 0) return VerificationStatus.ToVerify;
        if (list.Any(x => x == VerificationStatus.NotIncluded)) return VerificationStatus.NotIncluded;
        if (list.All(IsResolved)) return VerificationStatus.Confirmed;
        return list.Any(x => x == VerificationStatus.InProgress) ? VerificationStatus.InProgress : VerificationStatus.ToVerify;
    }
}
