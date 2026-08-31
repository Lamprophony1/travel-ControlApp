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
public sealed record PassengerEvidenceState(
    bool HasAirTicketEvidence = false,
    bool HasHotelVoucherEvidence = false,
    bool HasBaggageEvidence = false);

public static class BusinessRules
{
    public const string TopTravelPropertyAlert = "Propiedad específica del hotel pendiente de confirmar con Top Travel";
    public const string StaleDocumentationAlert = "Confirmación documental legacy ignorada";

    private static readonly string[] PropertyPlaceholders =
    [
        "PROPIEDAD EXACTA PENDIENTE", "POR CONFIRMAR", "SIN DEFINIR", "PENDIENTE", "A DEFINIR"
    ];

    public static PassportStatus CalculatePassport(Passenger passenger, DateOnly returnDate, int warningDays, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(passenger.PassportNumber) || string.IsNullOrWhiteSpace(passenger.Nationality)
            || passenger.BirthDate is null || passenger.PassportExpiry is null) return PassportStatus.Incomplete;
        if (passenger.PassportExpiry < returnDate) return PassportStatus.Expired;
        return passenger.PassportExpiry.Value.DayNumber - today.DayNumber <= warningDays
            ? PassportStatus.ExpiringSoon : PassportStatus.Valid;
    }

    public static bool IsSpecificPropertyPending(string operatorName, string? hotel)
    {
        if (!string.Equals(TextNormalizer.Normalize(operatorName), "TOP TRAVEL", StringComparison.Ordinal)) return false;
        if (string.IsNullOrWhiteSpace(hotel)) return true;
        var normalized = TextNormalizer.Normalize(hotel);
        return PropertyPlaceholders.Any(x => normalized.Contains(x, StringComparison.Ordinal));
    }

    public static bool RoomCanBeConfirmed(RoomReservation? room, out string[] missing) => RoomCanBeConfirmed(room, false, out missing);

    public static bool RoomCanBeConfirmed(RoomReservation? room, bool hasHotelVoucherEvidence, out string[] missing)
    {
        var values = new List<string>();
        if (room is null) values.Add("habitación asignada");
        else
        {
            if (room.OperatorId == Guid.Empty) values.Add("operadora");
            if (room.CheckIn is null) values.Add("check-in");
            if (room.CheckOut is null) values.Add("check-out");
            if (string.IsNullOrWhiteSpace(room.RoomType)) values.Add("tipo de habitación");
            if (string.IsNullOrWhiteSpace(room.SourceReference) && !hasHotelVoucherEvidence) values.Add("fuente, referencia o voucher de hotel");
            if (room.CheckIn.HasValue && room.CheckOut.HasValue && room.CheckIn >= room.CheckOut) values.Add("fechas válidas");
            if (room.Passengers.Count > room.ExpectedCapacity
                && (!room.CapacityOverride || string.IsNullOrWhiteSpace(room.CapacityOverrideReason)))
                values.Add("capacidad compatible o excepción justificada");
        }
        missing = [.. values];
        return values.Count == 0;
    }

    public static bool FlightStructureCanBeConfirmed(FlightBooking booking, out string[] missing)
    {
        var values = new List<string>();
        if (string.IsNullOrWhiteSpace(booking.Airline)) values.Add("aerolínea");
        if (string.IsNullOrWhiteSpace(booking.Pnr)) values.Add("PNR");
        if (!booking.Segments.Any(x => x.Type == SegmentType.Outbound)) values.Add("segmento de ida");
        if (!booking.Segments.Any(x => x.Type == SegmentType.Return)) values.Add("segmento de regreso");
        if (booking.Segments.Any(SegmentIsIncomplete)) values.Add("estructura válida de segmentos");
        if (booking.Segments.GroupBy(x => x.Sequence).Any(x => x.Count() > 1)) values.Add("secuencia de segmentos sin duplicados");
        missing = [.. values.Distinct()];
        return missing.Length == 0;
    }

    public static bool FlightCanBeConfirmed(FlightBooking booking, PassengerFlight passengerFlight, out string[] missing)
    {
        var values = new List<string>();
        if (string.IsNullOrWhiteSpace(booking.Pnr)) values.Add("PNR");
        if (string.IsNullOrWhiteSpace(booking.Airline)) values.Add("aerolínea");
        if (passengerFlight.TicketStatus != VerificationStatus.Confirmed) values.Add("estado de ticket confirmado");
        missing = [.. values.Distinct()];
        return missing.Length == 0;
    }

    public static bool BaggageCanBeConfirmed(BaggageEntitlement baggage, bool hasEffectiveTicket, out string[] missing)
    {
        var values = new List<string>();
        if (baggage.FlightBookingId is null) values.Add("reserva aérea asociada");
        if (!hasEffectiveTicket) values.Add("ticket confirmado efectivamente");
        if (baggage.CheckedBagCount < 1) values.Add("al menos una maleta");
        if (baggage.WeightPerBagKg < 23) values.Add("peso mínimo de 23 kg");
        if (!(baggage.AppliesOutbound && baggage.AppliesReturn) && string.IsNullOrWhiteSpace(baggage.ExceptionReason))
            values.Add("ida y regreso o excepción documentada");
        missing = [.. values];
        return values.Count == 0;
    }

    public static bool BaggageCanBeConfirmed(FlightBooking booking, out string[] missing)
    {
        var values = new List<string>();
        if (!booking.CheckedBagIncluded) values.Add("maleta incluida");
        if (booking.CheckedBagCount < 1) values.Add("al menos una maleta");
        if (booking.CheckedBagWeightKg < 23) values.Add("peso mínimo de 23 kg");
        if (!booking.BaggageAppliesOutbound) values.Add("ida");
        if (!booking.BaggageAppliesReturn) values.Add("regreso");
        missing = [.. values];
        return values.Count == 0;
    }

    public static PassengerComputedState CalculatePassenger(Passenger passenger, DateOnly today, bool hasAirTicketEvidence = false) =>
        CalculatePassenger(passenger, today, new PassengerEvidenceState(HasAirTicketEvidence: hasAirTicketEvidence));

    public static PassengerComputedState CalculatePassenger(Passenger passenger, DateOnly today, PassengerEvidenceState evidence)
    {
        var returnDate = passenger.RoomReservation?.CheckOut ?? passenger.Trip.EndDate;
        var passport = CalculatePassport(passenger, returnDate, passenger.Trip.PassportWarningDays, today);
        var passportRequirement = passport == PassportStatus.Valid
            ? VerificationStatus.Confirmed
            : passport == PassportStatus.Expired ? VerificationStatus.NotIncluded : VerificationStatus.ToVerify;

        var room = EffectiveRoom(passenger.RoomReservation, evidence.HasHotelVoucherEvidence);
        var flight = EffectiveFlight(passenger.PassengerFlights);
        var baggage = EffectiveBaggage(passenger.PassengerFlights);
        var documentation = EffectiveDocumentation(passenger.PassengerFlights);

        var requirements = new[]
        {
            new RequirementState("passport", passportRequirement, "Pasaporte", PassportReason(passport)),
            documentation,
            room,
            flight,
            baggage
        };

        var alerts = new List<string>();
        if (passenger.RoomReservation is null) alerts.Add("Pasajero sin habitación");
        if (passenger.RoomReservation?.SpecificPropertyPending == true) alerts.Add(TopTravelPropertyAlert);
        if (passport == PassportStatus.Expired) alerts.Add("Pasaporte vencido antes del regreso");
        if (passenger.RoomReservation?.CheckIn >= passenger.RoomReservation?.CheckOut) alerts.Add("Fechas de alojamiento inconsistentes");
        if (passenger.RoomReservation is { } assignedRoom && assignedRoom.Passengers.Count > assignedRoom.ExpectedCapacity
            && (!assignedRoom.CapacityOverride || string.IsNullOrWhiteSpace(assignedRoom.CapacityOverrideReason)))
            alerts.Add("Ocupación incompatible con la capacidad");
        if (requirements.Any(x => x.Status == VerificationStatus.NotIncluded)) alerts.Add("Existe un requisito no incluido");

        var resolved = requirements.Count(IsResolved);
        var progress = resolved * 20;
        var critical = alerts.Any(x => x != TopTravelPropertyAlert);
        var overall = critical ? PassengerOverallStatus.Attention
            : resolved == requirements.Length ? PassengerOverallStatus.Ready : PassengerOverallStatus.Pending;
        return new(passport, overall, progress, requirements, alerts.Distinct().ToArray());
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
        if (overall != TripOverallStatus.Ready && progress == 100) progress = 99;
        return new(overall, progress, allPassengersReady, transferConfirmed, alerts);
    }

    public static VerificationStatus DeriveFlightBookingStatus(FlightBooking booking)
    {
        var hasRealData = !string.IsNullOrWhiteSpace(booking.Pnr) || !string.IsNullOrWhiteSpace(booking.Airline)
            || !string.IsNullOrWhiteSpace(booking.GeneralReference) || !string.IsNullOrWhiteSpace(booking.SourceReference)
            || booking.Segments.Count > 0 || booking.PassengerFlights.Count > 0;
        if (!hasRealData) return VerificationStatus.ToVerify;
        if (string.IsNullOrWhiteSpace(booking.Pnr) || string.IsNullOrWhiteSpace(booking.Airline)
            || booking.PassengerFlights.Count == 0)
            return VerificationStatus.InProgress;
        return booking.PassengerFlights.All(link => FlightCanBeConfirmed(booking, link, out _))
            ? VerificationStatus.Confirmed
            : VerificationStatus.InProgress;
    }

    public static bool IsResolved(RequirementState requirement) => IsResolved(requirement.Status)
        && (requirement.Status != VerificationStatus.NotApplicable || !string.IsNullOrWhiteSpace(requirement.Reason));
    public static bool IsResolved(VerificationStatus status) => status is VerificationStatus.Confirmed or VerificationStatus.NotApplicable;

    private static RequirementState EffectiveRoom(RoomReservation? room, bool hasHotelVoucherEvidence)
    {
        if (room?.Status == VerificationStatus.NotApplicable)
            return new("room", VerificationStatus.NotApplicable, "Habitación", FirstNonBlank(room.CapacityOverrideReason, room.Notes));
        if (room?.Status == VerificationStatus.Confirmed)
        {
            var valid = RoomCanBeConfirmed(room, hasHotelVoucherEvidence, out var missing);
            return new("room", valid ? VerificationStatus.Confirmed : VerificationStatus.ToVerify, "Habitación", JoinMissing(missing));
        }
        return new("room", room?.Status ?? VerificationStatus.ToVerify, "Habitación");
    }

    private static RequirementState EffectiveFlight(IEnumerable<PassengerFlight> passengerFlights)
    {
        var links = passengerFlights.ToArray();
        if (links.Length == 0) return new("flight", VerificationStatus.ToVerify, "Ticket de vuelo", "Falta reserva aérea asociada");
        var results = links.Select(link =>
        {
            if (link.TicketStatus == VerificationStatus.NotApplicable)
                return new RequirementState("flight", VerificationStatus.NotApplicable, "Ticket de vuelo", link.Notes);
            if (link.TicketStatus == VerificationStatus.Confirmed)
            {
                var valid = FlightCanBeConfirmed(link.FlightBooking, link, out var missing);
                return new RequirementState("flight", valid ? VerificationStatus.Confirmed : VerificationStatus.ToVerify,
                    "Ticket de vuelo", JoinMissing(missing));
            }
            return new RequirementState("flight", link.TicketStatus, "Ticket de vuelo");
        }).ToArray();
        return Aggregate("flight", "Ticket de vuelo", results);
    }

    private static RequirementState EffectiveBaggage(IEnumerable<PassengerFlight> passengerFlights)
    {
        var bookings = passengerFlights.Select(x => x.FlightBooking).DistinctBy(x => x.Id).ToArray();
        if (bookings.Length == 0) return new("baggage", VerificationStatus.ToVerify, "Maleta de 23 kg", "Falta reserva aérea asociada");
        var results = bookings.Select(booking =>
        {
            if (booking.BaggageStatus == VerificationStatus.NotApplicable)
                return new RequirementState("baggage", VerificationStatus.NotApplicable, "Maleta de 23 kg", booking.BaggageNotes);
            if (booking.BaggageStatus == VerificationStatus.Confirmed)
            {
                var valid = BaggageCanBeConfirmed(booking, out var missing);
                return new RequirementState("baggage", valid ? VerificationStatus.Confirmed : VerificationStatus.ToVerify,
                    "Maleta de 23 kg", JoinMissing(missing));
            }
            return new RequirementState("baggage", booking.BaggageStatus, "Maleta de 23 kg");
        }).ToArray();
        if (results.Any(x => x.Status == VerificationStatus.NotIncluded))
            return new("baggage", VerificationStatus.NotIncluded, "Maleta de 23 kg");
        if (results.Any(x => x.Status == VerificationStatus.ToVerify))
            return new("baggage", VerificationStatus.ToVerify, "Maleta de 23 kg", results.Select(x => x.Reason).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
        if (results.Any(x => x.Status == VerificationStatus.InProgress))
            return new("baggage", VerificationStatus.InProgress, "Maleta de 23 kg");
        if (results.All(x => x.Status == VerificationStatus.NotApplicable))
            return new("baggage", VerificationStatus.NotApplicable, "Maleta de 23 kg", string.Join("; ", results.Select(x => x.Reason).Where(x => !string.IsNullOrWhiteSpace(x))));
        return new("baggage", VerificationStatus.Confirmed, "Maleta de 23 kg");
    }

    private static RequirementState EffectiveDocumentation(IEnumerable<PassengerFlight> passengerFlights)
    {
        var links = passengerFlights.ToArray();
        if (links.Length == 0)
            return new("documentation", VerificationStatus.ToVerify, "Documentación", "Falta acceso al ticket");
        if (links.All(x => x.TicketAccessStatus == TicketAccessStatus.Verified
            && !string.IsNullOrWhiteSpace(x.TicketAccessUrl)))
            return new("documentation", VerificationStatus.Confirmed, "Documentación");
        if (links.All(x => (x.TicketAccessStatus is TicketAccessStatus.Generated or TicketAccessStatus.Verified)
            && !string.IsNullOrWhiteSpace(x.TicketAccessUrl)))
            return new("documentation", VerificationStatus.InProgress, "Documentación", "Acceso generado, pendiente de verificar");
        return new("documentation", VerificationStatus.ToVerify, "Documentación", "Falta acceso verificado al ticket");
    }

    private static RequirementState Aggregate(string key, string label, IReadOnlyList<RequirementState> values)
    {
        if (values.All(IsResolved))
        {
            if (values.All(x => x.Status == VerificationStatus.NotApplicable))
                return new(key, VerificationStatus.NotApplicable, label, string.Join("; ", values.Select(x => x.Reason).Where(x => !string.IsNullOrWhiteSpace(x))));
            return new(key, VerificationStatus.Confirmed, label);
        }
        if (values.Any(x => x.Status == VerificationStatus.NotIncluded)) return new(key, VerificationStatus.NotIncluded, label);
        if (values.Any(x => x.Status == VerificationStatus.InProgress)) return new(key, VerificationStatus.InProgress, label);
        return new(key, VerificationStatus.ToVerify, label, values.Select(x => x.Reason).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static bool SegmentIsIncomplete(FlightSegment segment) => string.IsNullOrWhiteSpace(segment.FlightNumber)
        || string.IsNullOrWhiteSpace(segment.OriginAirport) || string.IsNullOrWhiteSpace(segment.DestinationAirport)
        || segment.DepartureAt is null || segment.ArrivalAt is null || segment.ArrivalAt <= segment.DepartureAt;
    private static string PassportReason(PassportStatus status) => status switch
    {
        PassportStatus.Valid => "Vigente",
        PassportStatus.Expired => "Vencido antes del regreso",
        PassportStatus.ExpiringSoon => "Por vencer",
        _ => "Datos incompletos"
    };
    private static string? JoinMissing(IEnumerable<string> missing)
    {
        var values = missing.ToArray();
        return values.Length == 0 ? null : "Falta: " + string.Join(", ", values);
    }
    private static string? FirstNonBlank(params string?[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
}
