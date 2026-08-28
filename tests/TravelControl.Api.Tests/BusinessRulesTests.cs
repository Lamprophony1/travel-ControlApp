using TravelControl.Api.Domain;
using TravelControl.Api.Services;
using Xunit;

namespace TravelControl.Api.Tests;

public sealed class BusinessRulesTests
{
    private static Passenger Passenger() => new()
    {
        FullName = "Persona de Prueba", NormalizedName = "PERSONA DE PRUEBA",
        Trip = new Trip { Name = "Viaje", Destination = "Destino", EndDate = new DateOnly(2026, 9, 15), PassportWarningDays = 180 }
    };

    [Fact]
    public void Passport_is_incomplete_when_required_data_is_missing()
    {
        var status = BusinessRules.CalculatePassport(Passenger(), new DateOnly(2026, 9, 15), 180, new DateOnly(2026, 1, 1));
        Assert.Equal(PassportStatus.Incomplete, status);
    }

    [Fact]
    public void Passport_is_expired_when_expiry_precedes_return()
    {
        var p = Passenger(); p.PassportNumber = "TEST123"; p.Nationality = "Paraguaya"; p.BirthDate = new DateOnly(1990, 1, 1); p.PassportExpiry = new DateOnly(2026, 9, 14);
        Assert.Equal(PassportStatus.Expired, BusinessRules.CalculatePassport(p, new DateOnly(2026, 9, 15), 180, new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void Room_nights_are_calculated_from_dates()
    {
        var room = new RoomReservation { InternalCode = "TEST-01", CheckIn = new DateOnly(2026, 9, 6), CheckOut = new DateOnly(2026, 9, 11) };
        Assert.Equal(5, room.Nights);
    }

    [Fact]
    public void Flight_confirmation_lists_missing_fields()
    {
        var booking = new FlightBooking(); var link = new PassengerFlight();
        Assert.False(BusinessRules.FlightCanBeConfirmed(booking, link, out var missing));
        Assert.Contains("PNR", missing); Assert.Contains("segmento de ida", missing); Assert.Contains("segmento de regreso", missing);
    }

    [Fact]
    public void Baggage_requires_23kg_both_directions_and_ticket()
    {
        var baggage = new BaggageEntitlement { CheckedBagCount = 1, WeightPerBagKg = 23, AppliesOutbound = true, AppliesReturn = true, VerifiedAt = DateTimeOffset.UtcNow };
        Assert.True(BusinessRules.BaggageCanBeConfirmed(baggage, true, out var missing)); Assert.Empty(missing);
    }

    [Fact]
    public void Complete_transfer_can_cover_both_directions()
    {
        var transfer = new TransferBooking { Company = "Empresa", VoucherCode = "V-1", Coverage = TransferCoverage.Both, VerifiedAt = DateTimeOffset.UtcNow };
        transfer.PassengerTransfers.Add(new PassengerTransfer());
        Assert.True(BusinessRules.TransferCanBeConfirmed(transfer, out _));
    }

    [Theory]
    [InlineData("  José   Pérez ", "JOSE PEREZ")]
    [InlineData("María\tRolón", "MARIA ROLON")]
    public void Names_are_normalized_without_losing_display_source(string source, string expected) => Assert.Equal(expected, TextNormalizer.Normalize(source));

    [Fact]
    public void Progress_uses_six_categories()
    {
        var p = Passenger();
        var state = BusinessRules.CalculatePassenger(p, new DateOnly(2026, 1, 1));
        Assert.Equal(6, state.Requirements.Count); Assert.Equal(0, state.ProgressPercent); Assert.Equal(PassengerOverallStatus.Attention, state.OverallStatus);
    }
}
