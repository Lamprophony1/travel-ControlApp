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
    public void Legacy_documentation_status_is_ignored()
    {
        var passenger = Passenger();
        passenger.DocumentationStatus = VerificationStatus.NotApplicable;
        passenger.DocumentationExceptionReason = "Excepción ficticia justificada";
        var documentation = BusinessRules.CalculatePassenger(passenger, new DateOnly(2026, 1, 1)).Requirements.Single(x => x.Key == "documentation");
        Assert.False(BusinessRules.IsResolved(documentation));
        Assert.Equal(VerificationStatus.ToVerify, documentation.Status);
    }

    [Fact]
    public void Stored_confirmations_do_not_override_invalid_effective_requirements()
    {
        var passenger = Passenger();
        var room = new RoomReservation { InternalCode = "TEST", Status = VerificationStatus.Confirmed, ExpectedCapacity = 1 };
        room.Passengers.Add(passenger); passenger.RoomReservation = room;
        var booking = new FlightBooking { TripId = Guid.NewGuid(), Status = VerificationStatus.Confirmed,
            BaggageStatus = VerificationStatus.Confirmed, CheckedBagIncluded = true, CheckedBagCount = 1,
            CheckedBagWeightKg = 20, BaggageAppliesOutbound = true, BaggageAppliesReturn = true };
        var link = new PassengerFlight { Passenger = passenger, FlightBooking = booking, TicketStatus = VerificationStatus.Confirmed };
        passenger.PassengerFlights.Add(link);
        var state = BusinessRules.CalculatePassenger(passenger, new DateOnly(2026, 1, 1));
        Assert.Equal(VerificationStatus.ToVerify, state.Requirements.Single(x => x.Key == "room").Status);
        Assert.Equal(VerificationStatus.ToVerify, state.Requirements.Single(x => x.Key == "flight").Status);
        Assert.Equal(VerificationStatus.ToVerify, state.Requirements.Single(x => x.Key == "baggage").Status);
    }

    [Fact]
    public void Documentation_depends_only_on_verified_ticket_access()
    {
        var passenger = Passenger();
        passenger.DocumentationStatus = VerificationStatus.Confirmed;
        passenger.PassportReviewStatus = VerificationStatus.Confirmed;
        var link = ValidBooking(passenger, "DOC-001", VerificationStatus.Confirmed);

        Assert.Equal(VerificationStatus.ToVerify, Documentation(passenger).Status);
        link.TicketAccessUrl = "https://mytrips.copaair.com/trip-detail/ABC123/FICTIONAL";
        link.TicketAccessStatus = TicketAccessStatus.Generated;
        Assert.Equal(VerificationStatus.InProgress, Documentation(passenger).Status);
        link.TicketAccessStatus = TicketAccessStatus.Verified;
        Assert.Equal(VerificationStatus.Confirmed, Documentation(passenger).Status);
        passenger.PassportReviewStatus = VerificationStatus.NotIncluded;
        passenger.RoomReservation = null;
        Assert.Equal(VerificationStatus.Confirmed, Documentation(passenger, new PassengerEvidenceState(HasAirTicketEvidence: false)).Status);
    }

    [Fact]
    public void Every_associated_flight_and_baggage_record_must_be_resolved()
    {
        var passenger = Passenger();
        var validBooking = ValidBooking(passenger, "VALID-001", VerificationStatus.Confirmed);
        var pendingBooking = ValidBooking(passenger, "PENDING-001", VerificationStatus.ToVerify);
        validBooking.FlightBooking.BaggageStatus = VerificationStatus.Confirmed;
        validBooking.FlightBooking.CheckedBagIncluded = true;
        validBooking.FlightBooking.CheckedBagCount = 1;
        validBooking.FlightBooking.CheckedBagWeightKg = 23;
        validBooking.FlightBooking.BaggageAppliesOutbound = true;
        validBooking.FlightBooking.BaggageAppliesReturn = true;
        pendingBooking.FlightBooking.BaggageStatus = VerificationStatus.ToVerify;

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
        var booking = new FlightBooking { TripId = Guid.NewGuid(), Pnr = "FIXTURE-PNR", Airline = "Aerolínea ficticia",
            BaggageStatus = VerificationStatus.Confirmed, CheckedBagIncluded = true, CheckedBagCount = 1,
            CheckedBagWeightKg = 23, BaggageAppliesOutbound = true, BaggageAppliesReturn = true };
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
        var booking = new FlightBooking { TripId = Guid.NewGuid(), Pnr = "FIXTURE-PNR", Airline = "Aerolínea ficticia",
            BaggageStatus = VerificationStatus.Confirmed, CheckedBagIncluded = true, CheckedBagCount = 1,
            CheckedBagWeightKg = 23, BaggageAppliesOutbound = true, BaggageAppliesReturn = true };
        var link = new PassengerFlight { Passenger = passenger, FlightBooking = booking, FlightBookingId = booking.Id, TicketStatus = VerificationStatus.Confirmed };
        passenger.PassengerFlights.Add(link);
        var state = BusinessRules.CalculatePassenger(passenger, new DateOnly(2026, 1, 1));

        Assert.True(BusinessRules.IsResolved(state.Requirements.Single(x => x.Key == "baggage")));
        Assert.Equal(VerificationStatus.ToVerify, passenger.DocumentationStatus);
    }

    [Fact]
    public void Multiple_pnr_baggage_uses_required_precedence()
    {
        var passenger = Passenger();
        var confirmed = ValidBooking(passenger, "ONE", VerificationStatus.Confirmed).FlightBooking;
        confirmed.BaggageStatus = VerificationStatus.Confirmed;
        confirmed.CheckedBagIncluded = true;
        confirmed.CheckedBagCount = 1;
        confirmed.CheckedBagWeightKg = 23;
        confirmed.BaggageAppliesOutbound = confirmed.BaggageAppliesReturn = true;
        var second = ValidBooking(passenger, "TWO", VerificationStatus.Confirmed).FlightBooking;
        second.BaggageStatus = VerificationStatus.InProgress;
        Assert.Equal(VerificationStatus.InProgress, Baggage(passenger).Status);
        second.BaggageStatus = VerificationStatus.ToVerify;
        Assert.Equal(VerificationStatus.ToVerify, Baggage(passenger).Status);
        second.BaggageStatus = VerificationStatus.NotIncluded;
        Assert.Equal(VerificationStatus.NotIncluded, Baggage(passenger).Status);
    }

    [Fact]
    public void One_booking_baggage_value_applies_to_all_six_linked_passengers()
    {
        var booking = new FlightBooking
        {
            TripId = Guid.NewGuid(), Airline = "Aerolínea ficticia", Pnr = "GROUP-SIX",
            Status = VerificationStatus.Confirmed, BaggageStatus = VerificationStatus.Confirmed,
            CheckedBagIncluded = true, CheckedBagCount = 1, CheckedBagWeightKg = 23,
            BaggageAppliesOutbound = true, BaggageAppliesReturn = true
        };
        var passengers = Enumerable.Range(1, 6).Select(index => new Passenger
        {
            FullName = $"Persona {index}", NormalizedName = $"PERSONA {index}", Trip = Passenger().Trip
        }).ToArray();
        foreach (var passenger in passengers)
        {
            var link = new PassengerFlight
            {
                Passenger = passenger, FlightBooking = booking, FlightBookingId = booking.Id,
                TicketStatus = VerificationStatus.Confirmed
            };
            passenger.PassengerFlights.Add(link);
            booking.PassengerFlights.Add(link);
        }

        Assert.All(passengers, passenger => Assert.Equal(VerificationStatus.Confirmed, Baggage(passenger).Status));
        Assert.Single(passengers.SelectMany(x => x.PassengerFlights).Select(x => x.FlightBooking).Distinct());
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

    private static RequirementState Documentation(Passenger passenger, PassengerEvidenceState? evidence = null) =>
        BusinessRules.CalculatePassenger(passenger, new DateOnly(2026, 1, 1), evidence ?? new PassengerEvidenceState())
            .Requirements.Single(x => x.Key == "documentation");
    private static RequirementState Baggage(Passenger passenger) =>
        BusinessRules.CalculatePassenger(passenger, new DateOnly(2026, 1, 1)).Requirements.Single(x => x.Key == "baggage");

    [Theory]
    [InlineData("  José   Pérez ", "JOSE PEREZ")]
    [InlineData("María\tRolón", "MARIA ROLON")]
    public void Names_are_normalized(string source, string expected) => Assert.Equal(expected, TextNormalizer.Normalize(source));
}
