using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;
using TravelControl.Infrastructure.Services;
using Xunit;

namespace TravelControl.Api.Tests;

public sealed class PublicApiTests
{
    private static readonly string[] ForbiddenKeys =
    [
        "passportNumber", "maskedPassport", "birthDate", "nationality", "passportExpiry", "phone", "email",
        "dietaryRestrictions", "notes", "nextAction", "nextActionDueDate", "pnr", "electronicTicketNumber",
        "sourceReference", "operatorContact", "attachments", "followUps", "audit", "updatedBy", "userName"
    ];

    [Fact]
    public async Task Public_reads_are_anonymous_and_private_endpoints_stay_protected()
    {
        await using var factory = new TravelControlWebFactory();
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;
        var passengerId = await SeedPassengerAsync(factory.Services, ct);
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<PublicReadService>().GetDashboardAsync(ct);

        var dashboard = await client.GetAsync("/api/public/dashboard", ct);
        Assert.True(dashboard.IsSuccessStatusCode, await dashboard.Content.ReadAsStringAsync(ct));
        Assert.True(dashboard.Headers.CacheControl?.NoStore);
        Assert.Contains(dashboard.Headers.GetValues("X-Robots-Tag"), x => x.Contains("noindex", StringComparison.OrdinalIgnoreCase));
        var list = await client.GetAsync("/api/public/passengers?page=1&pageSize=1", ct);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var detail = await client.GetAsync($"/api/public/passengers/{passengerId}", ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dashboard", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/passengers?page=1&pageSize=1", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/baggage", new { }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync("/api/transfer", new { isConfirmed = true, version = 1 }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"/api/flights/{Guid.NewGuid()}", ct)).StatusCode);

        var dashboardJson = JsonDocument.Parse(await dashboard.Content.ReadAsStringAsync(ct)).RootElement;
        AssertNoForbiddenKeys(dashboardJson);
        AssertNoForbiddenKeys(JsonDocument.Parse(await list.Content.ReadAsStringAsync(ct)).RootElement);
        AssertNoForbiddenKeys(JsonDocument.Parse(await detail.Content.ReadAsStringAsync(ct)).RootElement);
        var roomKpi = dashboardJson.GetProperty("kpis").EnumerateArray().Single(x => x.GetProperty("key").GetString() == "rooms");
        Assert.Equal(0, roomKpi.GetProperty("value").GetInt32());
        Assert.Single(dashboardJson.GetProperty("alerts").EnumerateArray(), x => x.GetString() == "Transfer grupal pendiente");
    }

    [Fact]
    public async Task Public_page_size_is_limited_and_search_does_not_accept_sensitive_fields()
    {
        await using var factory = new TravelControlWebFactory();
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;
        await SeedPassengerAsync(factory.Services, ct);
        var byPassport = await client.GetFromJsonAsync<JsonElement>("/api/public/passengers?search=TEST-PASSPORT-999&pageSize=500", ct);
        Assert.Equal(50, byPassport.GetProperty("pageSize").GetInt32());
        Assert.Equal(0, byPassport.GetProperty("total").GetInt32());
        var byName = await client.GetFromJsonAsync<JsonElement>("/api/public/passengers?search=Persona%20ficticia", ct);
        Assert.Equal(1, byName.GetProperty("total").GetInt32());
    }

    private static async Task<Guid> SeedPassengerAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trip = await db.Trips.SingleAsync(x => x.IsActive, ct);
        var op = await db.Operators.SingleAsync(x => x.Name == "Top Travel", ct);
        var room = new RoomReservation
        {
            TripId = trip.Id, InternalCode = "TEST-01", OperatorId = op.Id, Status = VerificationStatus.Confirmed,
            Hotel = "Hotel ficticio", RoomType = "Doble", ExpectedCapacity = 2,
            CheckIn = trip.StartDate, CheckOut = trip.EndDate, SourceReference = null
        };
        var passenger = new Passenger
        {
            TripId = trip.Id, FullName = "Persona ficticia", NormalizedName = "PERSONA FICTICIA",
            PassportNumber = "TEST-PASSPORT-999", NormalizedPassportNumber = "TEST-PASSPORT-999",
            BirthDate = new DateOnly(1990, 1, 1), Nationality = "Ficticia", PassportExpiry = trip.EndDate.AddYears(2),
            PrimaryOperatorId = op.Id, RoomReservation = room, Phone = "000", Email = "fixture@example.test"
        };
        db.Passengers.Add(passenger);
        await db.SaveChangesAsync(ct);
        return passenger.Id;
    }

    private static void AssertNoForbiddenKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                Assert.DoesNotContain(ForbiddenKeys, key => string.Equals(key, property.Name, StringComparison.OrdinalIgnoreCase));
                AssertNoForbiddenKeys(property.Value);
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) AssertNoForbiddenKeys(item);
    }
}
