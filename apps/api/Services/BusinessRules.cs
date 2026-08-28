using TravelControl.Api.Domain;

namespace TravelControl.Api.Services;

public sealed record RequirementState(string Key, VerificationStatus Status, string Label, string? Reason = null);
public sealed record PassengerComputedState(
    PassportStatus PassportStatus,
    PassengerOverallStatus OverallStatus,
    int ProgressPercent,
    IReadOnlyList<RequirementState> Requirements,
    IReadOnlyList<string> Alerts);

public static class BusinessRules
{
    public const string TopTravelPropertyAlert = "Propiedad específica de Grand Palladium pendiente de confirmar con Top Travel";

    public static PassportStatus CalculatePassport(Passenger p, DateOnly returnDate, int warningDays, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(p.PassportNumber) || string.IsNullOrWhiteSpace(p.Nationality)
            || p.BirthDate is null || p.PassportExpiry is null) return PassportStatus.Incomplete;
        if (p.PassportExpiry < returnDate) return PassportStatus.Expired;
        return p.PassportExpiry.Value.DayNumber - today.DayNumber <= warningDays
            ? PassportStatus.ExpiringSoon : PassportStatus.Valid;
    }

    public static bool RoomCanBeConfirmed(RoomReservation? room, out string[] missing)
    {
        var m = new List<string>();
        if (room is null) m.Add("habitación asignada");
        else
        {
            if (room.OperatorId == Guid.Empty) m.Add("operadora");
            if (room.CheckIn is null) m.Add("check-in");
            if (room.CheckOut is null) m.Add("check-out");
            if (string.IsNullOrWhiteSpace(room.RoomType)) m.Add("tipo de habitación");
            if (string.IsNullOrWhiteSpace(room.SourceReference)) m.Add("fuente o referencia");
            if (room.CheckIn >= room.CheckOut) m.Add("fechas válidas");
            if (!room.CapacityOverride && room.Passengers.Count > room.ExpectedCapacity) m.Add("capacidad compatible");
        }
        missing = [.. m];
        return m.Count == 0;
    }

    public static bool FlightCanBeConfirmed(FlightBooking booking, PassengerFlight passengerFlight, out string[] missing)
    {
        var m = new List<string>();
        if (string.IsNullOrWhiteSpace(booking.Airline)) m.Add("aerolínea");
        if (string.IsNullOrWhiteSpace(booking.Pnr)) m.Add("PNR");
        if (string.IsNullOrWhiteSpace(passengerFlight.ElectronicTicketNumber)) m.Add("ticket electrónico");
        if (!booking.Segments.Any(x => x.Type == SegmentType.Outbound)) m.Add("segmento de ida");
        if (!booking.Segments.Any(x => x.Type == SegmentType.Return)) m.Add("segmento de regreso");
        if (booking.VerifiedAt is null) m.Add("fecha de verificación");
        missing = [.. m];
        return m.Count == 0;
    }

    public static bool BaggageCanBeConfirmed(BaggageEntitlement baggage, bool hasConfirmedTicket, out string[] missing)
    {
        var m = new List<string>();
        if (!hasConfirmedTicket) m.Add("ticket confirmado o asociado");
        if (baggage.CheckedBagCount < 1) m.Add("al menos una maleta");
        if (baggage.WeightPerBagKg < 23) m.Add("peso mínimo de 23 kg");
        if (!(baggage.AppliesOutbound && baggage.AppliesReturn) && string.IsNullOrWhiteSpace(baggage.ExceptionReason))
            m.Add("cobertura ida y regreso o excepción");
        if (baggage.VerifiedAt is null) m.Add("fecha de verificación");
        missing = [.. m];
        return m.Count == 0;
    }

    public static bool TransferCanBeConfirmed(TransferBooking transfer, out string[] missing)
    {
        var m = new List<string>();
        if (string.IsNullOrWhiteSpace(transfer.Company)) m.Add("empresa");
        if (string.IsNullOrWhiteSpace(transfer.VoucherCode)) m.Add("voucher o referencia");
        if (transfer.PassengerTransfers.Count == 0) m.Add("pasajeros asociados");
        if (transfer.VerifiedAt is null) m.Add("fecha de verificación");
        missing = [.. m];
        return m.Count == 0;
    }

    public static PassengerComputedState CalculatePassenger(Passenger p, DateOnly today)
    {
        var returnDate = p.RoomReservation?.CheckOut ?? p.Trip.EndDate;
        var passport = CalculatePassport(p, returnDate, p.Trip.PassportWarningDays, today);
        var roomStatus = p.RoomReservation?.Status ?? VerificationStatus.ToVerify;
        var ticketStatuses = p.PassengerFlights.Select(x => x.TicketStatus).ToArray();
        var flightStatus = Aggregate(ticketStatuses);
        var baggageStatus = Aggregate(p.BaggageEntitlements.Select(x => x.Status));
        var transferStatus = AggregateTransfer(p.PassengerTransfers.Select(x => x.TransferBooking));
        var passportVerification = passport == PassportStatus.Valid ? VerificationStatus.Confirmed : VerificationStatus.ToVerify;
        if (passport == PassportStatus.Expired) passportVerification = VerificationStatus.NotIncluded;

        var requirements = new[]
        {
            new RequirementState("passport", passportVerification, "Pasaporte", passport.ToString()),
            new RequirementState("documentation", p.DocumentationStatus, "Documentación"),
            new RequirementState("room", roomStatus, "Habitación"),
            new RequirementState("flight", flightStatus, "Ticket de vuelo"),
            new RequirementState("baggage", baggageStatus, "Maleta de 23 kg"),
            new RequirementState("transfer", transferStatus, "Transfer")
        };

        var alerts = new List<string>();
        if (p.RoomReservation is null) alerts.Add("Pasajero sin habitación");
        if (p.RoomReservation?.SpecificPropertyPending == true) alerts.Add(TopTravelPropertyAlert);
        if (passport == PassportStatus.Expired) alerts.Add("Pasaporte vencido antes del regreso");
        if (p.RoomReservation?.CheckIn >= p.RoomReservation?.CheckOut) alerts.Add("Fechas de alojamiento inconsistentes");
        if (p.RoomReservation is { CapacityOverride: false } room && room.Passengers.Count > room.ExpectedCapacity)
            alerts.Add("Ocupación incompatible con la capacidad");
        if (requirements.Any(x => x.Status == VerificationStatus.NotIncluded)) alerts.Add("Existe un requisito no incluido");

        var resolved = requirements.Count(IsResolved);
        var progress = (int)Math.Round(resolved * 100m / requirements.Length, MidpointRounding.AwayFromZero);
        var critical = alerts.Any(x => x != TopTravelPropertyAlert);
        var overall = critical ? PassengerOverallStatus.Attention
            : requirements.All(IsResolved) ? PassengerOverallStatus.Ready : PassengerOverallStatus.Pending;
        return new PassengerComputedState(passport, overall, progress, requirements, alerts);
    }

    private static VerificationStatus Aggregate(IEnumerable<VerificationStatus> values)
    {
        var list = values.ToArray();
        if (list.Length == 0) return VerificationStatus.ToVerify;
        if (list.Any(x => x == VerificationStatus.NotIncluded)) return VerificationStatus.NotIncluded;
        if (list.All(IsResolved)) return VerificationStatus.Confirmed;
        if (list.Any(x => x == VerificationStatus.InProgress)) return VerificationStatus.InProgress;
        return VerificationStatus.ToVerify;
    }

    private static VerificationStatus AggregateTransfer(IEnumerable<TransferBooking> transfers)
    {
        var confirmed = transfers.Where(x => x.Status == VerificationStatus.Confirmed).ToArray();
        if (confirmed.Any(x => x.Coverage == TransferCoverage.Both)
            || (confirmed.Any(x => x.Coverage == TransferCoverage.Arrival) && confirmed.Any(x => x.Coverage == TransferCoverage.Departure)))
            return VerificationStatus.Confirmed;
        return Aggregate(transfers.Select(x => x.Status));
    }

    public static bool IsResolved(RequirementState r) => IsResolved(r.Status)
        && (r.Status != VerificationStatus.NotApplicable || !string.IsNullOrWhiteSpace(r.Reason));
    public static bool IsResolved(VerificationStatus status) => status is VerificationStatus.Confirmed or VerificationStatus.NotApplicable;
}

