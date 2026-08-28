using TravelControl.Api.Validation;
using TravelControl.Application.Contracts;
using TravelControl.Domain;
using Xunit;

namespace TravelControl.Api.Tests;

public sealed class ValidationTests
{
    [Fact]
    public void Confirmed_flight_requires_outbound_and_return_segments()
    {
        var request = new FlightBookingRequest(VerificationStatus.Confirmed, "Aerolínea ficticia", null, "PNRTEST", null, "Fixture", null, [], []);
        var result = new FlightBookingValidator().Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.ErrorMessage.Contains("ida"));
        Assert.Contains(result.Errors, x => x.ErrorMessage.Contains("regreso"));
    }
}
