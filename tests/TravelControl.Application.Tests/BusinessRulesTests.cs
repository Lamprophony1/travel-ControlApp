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

    [Theory]
    [InlineData("  José   Pérez ", "JOSE PEREZ")]
    [InlineData("María\tRolón", "MARIA ROLON")]
    public void Names_are_normalized(string source, string expected) => Assert.Equal(expected, TextNormalizer.Normalize(source));
}
