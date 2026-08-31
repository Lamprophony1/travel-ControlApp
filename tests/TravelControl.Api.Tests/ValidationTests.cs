using TravelControl.Api.Validation;
using TravelControl.Application.Contracts;
using TravelControl.Domain;
using Xunit;

namespace TravelControl.Api.Tests;

public sealed class ValidationTests
{
    [Fact]
    public void Confirmed_flight_allows_optional_itinerary_when_airline_and_pnr_exist()
    {
        var request = new FlightBookingRequest(VerificationStatus.Confirmed, "Aerolínea ficticia", null, "PNRTEST", null, "Fixture", null, [], []);
        var result = new FlightBookingValidator().Validate(request);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null, "PNRTEST")]
    [InlineData("Aerolínea ficticia", null)]
    public void Confirmed_flight_still_requires_airline_and_pnr(string? airline, string? pnr)
    {
        var request = new FlightBookingRequest(VerificationStatus.Confirmed, airline, null, pnr, null, "Fixture", null, [], []);
        Assert.False(new FlightBookingValidator().Validate(request).IsValid);
    }
}
