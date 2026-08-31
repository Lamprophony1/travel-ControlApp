using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;
using Xunit;

namespace TravelControl.Api.Tests;

public sealed class TicketAccessApiTests
{
    [Fact]
    public async Task Preview_is_sanitized_and_commit_never_invents_latam_order_id()
    {
        await using var factory = new TravelControlWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var ct = TestContext.Current.CancellationToken;
        var csrf = await AuthenticateAsync(client, ct);
        await SeedAsync(factory.Services, ct);

        var preview = await SendAsync(client, "/api/ticket-access/preview-generation", new { }, csrf, ct);
        Assert.True(preview.StatusCode == HttpStatusCode.OK, await preview.Content.ReadAsStringAsync(ct));
        var previewText = await preview.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("PNR", previewText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Fictional", previewText, StringComparison.OrdinalIgnoreCase);
        var counts = JsonDocument.Parse(previewText).RootElement;
        Assert.Equal(2, counts.GetProperty("ticketedPassengers").GetInt32());
        Assert.Equal(1, counts.GetProperty("copaGenerable").GetInt32());
        Assert.Equal(1, counts.GetProperty("latamWithoutOrderId").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(client, "/api/ticket-access/commit-generation", new { confirm = false }, csrf, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(client, "/api/ticket-access/commit-generation", new { confirm = true }, csrf, ct)).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var links = await db.PassengerFlights.Include(x => x.FlightBooking).ToListAsync(ct);
        var copa = links.Single(x => x.FlightBooking.Airline!.Contains("Copa"));
        var latam = links.Single(x => x.FlightBooking.Airline!.Contains("LATAM"));
        Assert.Equal(TicketAccessStatus.Generated, copa.TicketAccessStatus);
        Assert.Equal("Fictional", copa.BookingLookupLastName);
        Assert.NotNull(copa.TicketAccessUrl);
        Assert.Equal(TicketAccessStatus.Missing, latam.TicketAccessStatus);
        Assert.Null(latam.AirlineOrderId);
        Assert.Null(latam.TicketAccessUrl);
        var audit = await db.AuditLogs.SingleAsync(x => x.Action == "CommitGeneration", ct);
        Assert.DoesNotContain("https://", audit.NewValue, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FIXTURE", audit.NewValue, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task SeedAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trip = await db.Trips.SingleAsync(x => x.IsActive, ct);
        var copaPassenger = new Passenger { TripId = trip.Id, FullName = "Copa Fictional", NormalizedName = "COPA FICTIONAL", LastNames = "Fictional" };
        var copa = new FlightBooking { TripId = trip.Id, Airline = "Copa Airlines", Pnr = "FIXTURE1" };
        copa.PassengerFlights.Add(new PassengerFlight { Passenger = copaPassenger, TicketStatus = VerificationStatus.Confirmed });
        var latamPassenger = new Passenger { TripId = trip.Id, FullName = "Latam Fictional", NormalizedName = "LATAM FICTIONAL" };
        var latam = new FlightBooking { TripId = trip.Id, Airline = "LATAM Airlines", Pnr = "FIXTURE2" };
        latam.PassengerFlights.Add(new PassengerFlight { Passenger = latamPassenger, TicketStatus = VerificationStatus.Confirmed,
            BookingLookupLastName = "Fictional" });
        db.AddRange(copa, latam);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<string> AuthenticateAsync(HttpClient client, CancellationToken ct)
    {
        var csrfResponse = await client.GetAsync("/api/auth/csrf", ct);
        var csrf = JsonDocument.Parse(await csrfResponse.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("token").GetString()!;
        using var setup = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
        { Content = JsonContent.Create(new { email = "ticket-admin@example.test", password = "Strong-ticket-test!2026", displayName = "Admin" }) };
        setup.Headers.Add("X-XSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(setup, ct)).StatusCode);
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        { Content = JsonContent.Create(new { email = "ticket-admin@example.test", password = "Strong-ticket-test!2026", rememberMe = false }) };
        login.Headers.Add("X-XSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(login, ct)).StatusCode);
        var authenticated = await client.GetAsync("/api/auth/csrf", ct);
        return JsonDocument.Parse(await authenticated.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("token").GetString()!;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string path, object body, string csrf, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", csrf);
        return client.SendAsync(request, ct);
    }
}
