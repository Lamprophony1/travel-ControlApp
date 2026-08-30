using TravelControl.Application.Services;
using TravelControl.Domain;
using Xunit;

namespace TravelControl.Application.Tests;
public sealed class BusinessRulesTests
{
    private static Passenger Passenger() => new() { FullName = "Persona de prueba", NormalizedName = "PERSONA DE PRUEBA", Trip = new Trip { Name = "Viaje", Destination = "Destino", EndDate = new DateOnly(2026, 9, 15), PassportWarningDays = 180 } };

    [Fact]
    public void Passenger_progress_uses_exactly_five_categories()
    {
        var state = BusinessRules.CalculatePassenger(Passenger(), new DateOnly(2026, 1, 1));
        Assert.Equal(5, state.Requirements.Count);
        Assert.DoesNotContain(state.Requirements, x => x.Key.Contains("transfer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Trip_requires_the_single_global_transfer_confirmation()
    {
        var ready = new PassengerComputedState(PassportStatus.Valid, PassengerOverallStatus.Ready, 100, [], []);
        Assert.Equal(TripOverallStatus.Pending, BusinessRules.CalculateTrip([ready], false).OverallStatus);
        Assert.Equal(TripOverallStatus.Ready, BusinessRules.CalculateTrip([ready], true).OverallStatus);
    }

    [Fact]
    public void Not_applicable_is_resolved_only_with_a_reason()
    {
        var passenger = Passenger();
        passenger.DocumentationStatus = VerificationStatus.NotApplicable;
        var withoutReason = BusinessRules.CalculatePassenger(passenger, new DateOnly(2026, 1, 1)).Requirements.Single(x => x.Key == "documentation");
        Assert.False(BusinessRules.IsResolved(withoutReason));
        passenger.DocumentationExceptionReason = "Excepción ficticia justificada";
        var withReason = BusinessRules.CalculatePassenger(passenger, new DateOnly(2026, 1, 1)).Requirements.Single(x => x.Key == "documentation");
        Assert.True(BusinessRules.IsResolved(withReason));
        Assert.Equal(VerificationStatus.NotApplicable, withReason.Status);
    }

    [Fact]
    public void Stored_confirmations_do_not_override_invalid_effective_requirements()
    {
        var passenger = Passenger();
        var room = new RoomReservation { InternalCode = "TEST", Status = VerificationStatus.Confirmed, ExpectedCapacity = 1 };
        room.Passengers.Add(passenger); passenger.RoomReservation = room;
        var booking = new FlightBooking { TripId = Guid.NewGuid(), Status = VerificationStatus.Confirmed };
        var link = new PassengerFlight { Passenger = passenger, FlightBooking = booking, TicketStatus = VerificationStatus.Confirmed };
        passenger.PassengerFlights.Add(link);
        var baggage = new BaggageEntitlement
        {
            Passenger = passenger, FlightBookingId = booking.Id, Status = VerificationStatus.Confirmed,
            CheckedBagCount = 1, WeightPerBagKg = 20, AppliesOutbound = true, AppliesReturn = true
        };
        passenger.BaggageEntitlements.Add(baggage);
        var state = BusinessRules.CalculatePassenger(passenger, new DateOnly(2026, 1, 1));
        Assert.Equal(VerificationStatus.ToVerify, state.Requirements.Single(x => x.Key == "room").Status);
        Assert.Equal(VerificationStatus.ToVerify, state.Requirements.Single(x => x.Key == "flight").Status);
        Assert.Equal(VerificationStatus.ToVerify, state.Requirements.Single(x => x.Key == "baggage").Status);
    }

    [Fact]
    public void Documentation_confirmation_becomes_stale_when_dependencies_are_invalid()
    {
        var passenger = Passenger();
        passenger.DocumentationStatus = VerificationStatus.Confirmed;
        passenger.PassportReviewStatus = VerificationStatus.Confirmed;
        var state = BusinessRules.CalculatePassenger(passenger, new DateOnly(2026, 1, 1));
        Assert.Equal(VerificationStatus.ToVerify, state.Requirements.Single(x => x.Key == "documentation").Status);
        Assert.Contains(BusinessRules.StaleDocumentationAlert, state.Alerts);
    }

    [Fact]
    public void Every_associated_flight_and_baggage_record_must_be_resolved()
    {
        var passenger = Passenger();
        var validBooking = ValidBooking(passenger, "VALID-001", VerificationStatus.Confirmed);
        var pendingBooking = ValidBooking(passenger, "PENDING-001", VerificationStatus.ToVerify);
        passenger.BaggageEntitlements.Add(new BaggageEntitlement
        {
            Passenger = passenger, FlightBookingId = validBooking.FlightBookingId, Status = VerificationStatus.Confirmed,
            CheckedBagCount = 1, WeightPerBagKg = 23, AppliesOutbound = true, AppliesReturn = true
        });
        passenger.BaggageEntitlements.Add(new BaggageEntitlement
        {
            Passenger = passenger, FlightBookingId = pendingBooking.FlightBookingId, Status = VerificationStatus.ToVerify
        });

        var state = BusinessRules.CalculatePassenger(passenger, new DateOnly(2026, 1, 1));

        Assert.False(BusinessRules.IsResolved(state.Requirements.Single(x => x.Key == "flight")));
        Assert.False(BusinessRules.IsResolved(state.Requirements.Single(x => x.Key == "baggage")));
    }

    [Fact]
    public void Capacity_override_requires_a_reason()
    {
        var room = new RoomReservation
        {
            InternalCode = "TEST", OperatorId = Guid.NewGuid(), CheckIn = new DateOnly(2026, 1, 1), CheckOut = new DateOnly(2026, 1, 2),
            RoomType = "Doble", SourceReference = "Fixture", ExpectedCapacity = 1, CapacityOverride = true
        };
        room.Passengers.Add(Passenger()); room.Passengers.Add(Passenger());
        Assert.False(BusinessRules.RoomCanBeConfirmed(room, out _));
        room.CapacityOverrideReason = "Excepción ficticia";
        Assert.True(BusinessRules.RoomCanBeConfirmed(room, out _));
    }

    [Fact]
    public void Hotel_voucher_replaces_the_text_reference_for_an_otherwise_valid_room()
    {
        var passenger = Passenger();
        var room = new RoomReservation
        {
            InternalCode = "ROOM-VOUCHER", OperatorId = Guid.NewGuid(), Status = VerificationStatus.Confirmed,
            CheckIn = new DateOnly(2026, 9, 1), CheckOut = new DateOnly(2026, 9, 5),
            RoomType = "Doble", ExpectedCapacity = 2
        };
        room.Passengers.Add(passenger);
        passenger.RoomReservation = room;

        Assert.False(BusinessRules.RoomCanBeConfirmed(room, false, out _));
        Assert.True(BusinessRules.RoomCanBeConfirmed(room, true, out _));
        var state = BusinessRules.CalculatePassenger(passenger, new DateOnly(2026, 1, 1),
            new PassengerEvidenceState(HasHotelVoucherEvidence: true));
        Assert.Equal(VerificationStatus.Confirmed, state.Requirements.Single(x => x.Key == "room").Status);
    }

    [Fact]
    public void Trip_progress_is_capped_when_a_global_blocker_remains()
    {
        var ready = new PassengerComputedState(PassportStatus.Valid, PassengerOverallStatus.Ready, 100, [], []);
        var state = BusinessRules.CalculateTrip([ready], true, ["Propiedad pendiente"]);
        Assert.Equal(TripOverallStatus.Attention, state.OverallStatus);
        Assert.Equal(99, state.ProgressPercent);
    }

    [Fact]
    public void Flight_booking_status_is_derived_from_structure_passengers_and_effective_tickets()
    {
        var passenger = Passenger();
        var link = ValidBooking(passenger, "TICKET-FIXTURE", VerificationStatus.ToVerify);
        var booking = link.FlightBooking;
        booking.PassengerFlights.Add(link);
        Assert.Equal(VerificationStatus.InProgress, BusinessRules.DeriveFlightBookingStatus(booking));
        link.TicketStatus = VerificationStatus.Confirmed;
        Assert.Equal(VerificationStatus.Confirmed, BusinessRules.DeriveFlightBookingStatus(booking));
        booking.Pnr = null;
        Assert.Equal(VerificationStatus.InProgress, BusinessRules.DeriveFlightBookingStatus(booking));
        booking.Airline = null;
        booking.Segments.Clear();
        booking.PassengerFlights.Clear();
        Assert.Equal(VerificationStatus.ToVerify, BusinessRules.DeriveFlightBookingStatus(booking));
    }

    [Fact]
    public void Confirmed_ticket_requires_only_pnr_airline_and_confirmed_status()
    {
        var passenger = Passenger();
        var booking = new FlightBooking { TripId = Guid.NewGuid(), Pnr = "FIXTURE-PNR", Airline = "Aerolínea ficticia" };
        var link = new PassengerFlight { Passenger = passenger, FlightBooking = booking, TicketStatus = VerificationStatus.Confirmed };
        booking.PassengerFlights.Add(link);
        passenger.PassengerFlights.Add(link);

        Assert.True(BusinessRules.FlightCanBeConfirmed(booking, link, out var missing));
        Assert.Empty(missing);
        Assert.Null(link.ElectronicTicketNumber);
        Assert.Empty(booking.Segments);
        Assert.Equal(VerificationStatus.Confirmed, BusinessRules.DeriveFlightBookingStatus(booking));
        Assert.True(BusinessRules.IsResolved(BusinessRules.CalculatePassenger(passenger, new DateOnly(2026, 1, 1))
            .Requirements.Single(x => x.Key == "flight")));
    }

    [Theory]
    [InlineData(null, "Aerolínea ficticia", VerificationStatus.Confirmed)]
    [InlineData("FIXTURE-PNR", null, VerificationStatus.Confirmed)]
    [InlineData("FIXTURE-PNR", "Aerolínea ficticia", VerificationStatus.ToVerify)]
    public void Ticket_is_not_effective_when_an_authoritative_field_is_missing(
        string? pnr, string? airline, VerificationStatus status)
    {
        var booking = new FlightBooking { TripId = Guid.NewGuid(), Pnr = pnr, Airline = airline };
        var link = new PassengerFlight { FlightBooking = booking, TicketStatus = status };

        Assert.False(BusinessRules.FlightCanBeConfirmed(booking, link, out _));
        Assert.NotEqual(VerificationStatus.Confirmed, BusinessRules.DeriveFlightBookingStatus(booking));
    }

    [Fact]
    public void Effective_ticket_allows_baggage_without_electronic_number_or_segments()
    {
        var passenger = Passenger();
        var booking = new FlightBooking { TripId = Guid.NewGuid(), Pnr = "FIXTURE-PNR", Airline = "Aerolínea ficticia" };
        var link = new PassengerFlight { Passenger = passenger, FlightBooking = booking, FlightBookingId = booking.Id, TicketStatus = VerificationStatus.Confirmed };
        passenger.PassengerFlights.Add(link);
        var baggage = new BaggageEntitlement
        {
            Passenger = passenger, FlightBookingId = booking.Id, Status = VerificationStatus.Confirmed,
            CheckedBagCount = 1, WeightPerBagKg = 23, AppliesOutbound = true, AppliesReturn = true
        };
        passenger.BaggageEntitlements.Add(baggage);

        var state = BusinessRules.CalculatePassenger(passenger, new DateOnly(2026, 1, 1));

        Assert.True(BusinessRules.IsResolved(state.Requirements.Single(x => x.Key == "baggage")));
        Assert.Equal(VerificationStatus.ToVerify, passenger.DocumentationStatus);
    }

    private static PassengerFlight ValidBooking(Passenger passenger, string ticket, VerificationStatus status)
    {
        var booking = new FlightBooking
        {
            TripId = Guid.NewGuid(), Airline = "Aerolínea ficticia", Pnr = $"PNR-{ticket}", Status = VerificationStatus.Confirmed,
            Segments =
            [
                new FlightSegment { Type = SegmentType.Outbound, FlightNumber = "FX1", OriginAirport = "AAA", DestinationAirport = "BBB", DepartureAt = DateTimeOffset.UtcNow.AddDays(1), ArrivalAt = DateTimeOffset.UtcNow.AddDays(1).AddHours(2), Sequence = 1 },
                new FlightSegment { Type = SegmentType.Return, FlightNumber = "FX2", OriginAirport = "BBB", DestinationAirport = "AAA", DepartureAt = DateTimeOffset.UtcNow.AddDays(4), ArrivalAt = DateTimeOffset.UtcNow.AddDays(4).AddHours(2), Sequence = 2 }
            ]
        };
        var link = new PassengerFlight { Passenger = passenger, FlightBooking = booking, FlightBookingId = booking.Id, ElectronicTicketNumber = ticket, TicketStatus = status };
        passenger.PassengerFlights.Add(link);
        return link;
    }

    [Theory]
    [InlineData("  José   Pérez ", "JOSE PEREZ")]
    [InlineData("María\tRolón", "MARIA ROLON")]
    public void Names_are_normalized(string source, string expected) => Assert.Equal(expected, TextNormalizer.Normalize(source));
}
