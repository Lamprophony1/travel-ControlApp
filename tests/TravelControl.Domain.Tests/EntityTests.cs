using TravelControl.Domain;
using Xunit;

namespace TravelControl.Domain.Tests;
public sealed class EntityTests
{
    [Fact]
    public void Room_nights_are_derived_from_check_dates()
    {
        var room = new RoomReservation { InternalCode = "TEST-01", CheckIn = new DateOnly(2026, 9, 6), CheckOut = new DateOnly(2026, 9, 11) };
        Assert.Equal(5, room.Nights);
    }

    [Fact]
    public void Baggage_requires_23kg_in_both_directions()
    {
        var baggage = new BaggageEntitlement { CheckedBagCount = 1, WeightPerBagKg = 23, AppliesOutbound = true, AppliesReturn = true };
        Assert.True(baggage.Includes23Kg);
    }
}
