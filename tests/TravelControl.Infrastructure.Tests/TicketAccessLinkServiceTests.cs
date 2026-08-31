using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;
using TravelControl.Infrastructure.Services;
using Xunit;

namespace TravelControl.Infrastructure.Tests;

public sealed class TicketAccessLinkServiceTests
{
    [Fact]
    public void Copa_link_uses_real_pnr_and_normalized_explicit_last_name()
    {
        var booking = new FlightBooking { Airline = "Copa Airlines", Pnr = "ABC123" };
        var link = new PassengerFlight { BookingLookupLastName = "Fernández Escobar" };
        Assert.Equal("https://mytrips.copaair.com/trip-detail/ABC123/FERNANDEZESCOBAR",
            TicketAccessLinkService.BuildUrl(booking, link));
    }

    [Fact]
    public void Latam_link_requires_real_order_id_and_never_substitutes_pnr()
    {
        var booking = new FlightBooking { Airline = "LATAM Airlines", Pnr = "ABC123" };
        var link = new PassengerFlight { BookingLookupLastName = "Britos Romero", AirlineOrderId = "LA0000000TEST" };
        Assert.Equal("https://www.latamairlines.com/py/es/mis-viajes/second-detail?orderId=LA0000000TEST&lastname=britosromero",
            TicketAccessLinkService.BuildUrl(booking, link));
        link.AirlineOrderId = null;
        Assert.Null(TicketAccessLinkService.BuildUrl(booking, link));
    }

    [Theory]
    [InlineData("https://mytrips.copaair.com.evil.test/trip-detail/A/B")]
    [InlineData("http://mytrips.copaair.com/trip-detail/A/B")]
    [InlineData("https://www.latamairlines.com/py/es/mis-viajes/second-detail?orderId=&lastname=test")]
    public void Only_expected_official_destinations_are_accepted(string value) =>
        Assert.False(TicketAccessLinkService.IsSafeOfficialUrl(value));

    [Fact]
    public void Official_destination_must_match_the_booking_airline()
    {
        const string copa = "https://mytrips.copaair.com/trip-detail/ABC123/FICTIONAL";
        const string latam = "https://www.latamairlines.com/py/es/mis-viajes/second-detail?orderId=REAL&lastname=fictional";
        Assert.True(TicketAccessLinkService.IsSafeOfficialUrlForAirline("Copa Airlines", copa));
        Assert.True(TicketAccessLinkService.IsSafeOfficialUrlForAirline("LATAM Airlines", latam));
        Assert.False(TicketAccessLinkService.IsSafeOfficialUrlForAirline("LATAM Airlines", copa));
        Assert.False(TicketAccessLinkService.IsSafeOfficialUrlForAirline("Copa Airlines", latam));
    }

    [Fact]
    public void Structured_last_names_are_accepted_but_full_name_is_never_inferred()
    {
        var booking = new FlightBooking { Airline = "Copa Airlines", Pnr = "ABC123" };
        var structured = new PassengerFlight { Passenger = new Passenger
            { FullName = "Person Fictional", NormalizedName = "PERSON FICTIONAL", LastNames = "Fictional Compound" } };
        var unstructured = new PassengerFlight { Passenger = new Passenger
            { FullName = "Person Fictional", NormalizedName = "PERSON FICTIONAL" } };
        Assert.EndsWith("/FICTIONALCOMPOUND", TicketAccessLinkService.BuildUrl(booking, structured));
        Assert.Null(TicketAccessLinkService.BuildUrl(booking, unstructured));
    }

    [Fact]
    public async Task Public_tokens_are_unique_opaque_and_high_entropy()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var trip = new Trip { Name = "Token fixture", Destination = "Fixture" };
        var flight = new FlightBooking { Trip = trip, Pnr = "SECRET-PNR", Airline = "Copa Airlines" };
        for (var index = 0; index < 200; index++)
            flight.PassengerFlights.Add(new PassengerFlight
            {
                Passenger = new Passenger { Trip = trip, FullName = $"Person {index}", NormalizedName = $"PERSON {index}" }
            });
        db.Add(flight);
        await db.SaveChangesAsync(ct);

        var links = await db.PassengerFlights.AsNoTracking().ToListAsync(ct);
        Assert.Equal(links.Count, links.Select(x => x.PublicTicketAccessToken).Distinct().Count());
        Assert.All(links, link =>
        {
            Assert.True(link.PublicTicketAccessToken.Length >= 43);
            Assert.DoesNotContain(link.PassengerId.ToString("N"), link.PublicTicketAccessToken, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SECRET", link.PublicTicketAccessToken, StringComparison.OrdinalIgnoreCase);
        });
    }
}
