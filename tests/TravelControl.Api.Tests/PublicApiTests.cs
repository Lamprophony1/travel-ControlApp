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
    private const string Email = "admin.public@example.test";
    private const string Password = "Strong-test-password-123!";
    private static readonly string[] ForbiddenKeys =
    [
        "passportNumber", "maskedPassport", "birthDate", "nationality", "passportExpiry", "phone", "email",
        "dietaryRestrictions", "notes", "nextAction", "nextActionDueDate", "pnr", "electronicTicketNumber",
        "sourceReference", "operatorContact", "attachments", "followUps", "audit", "auditLog", "updatedBy", "userName",
        "normalizedPassportNumber", "securePath", "storedName", "originalName", "sha256", "attachmentId", "attachmentLinkId",
        "linkId", "evidenceType", "sourceId", "managePath", "affectedPassengerCount", "ticketVersion", "updatedById"
        , "orderId", "airlineOrderId", "bookingLookupLastName", "ticketAccessUrl"
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
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/rooms", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/flights", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/baggage", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/attachments", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/baggage", new { }, ct)).StatusCode);
        using (var import = new MultipartFormDataContent())
        {
            import.Add(new ByteArrayContent("row;name"u8.ToArray()), "file", "manifest.csv");
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostAsync("/api/imports/passenger-travel/preview", import, ct)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync("/api/transfer", new { isConfirmed = true, version = 1 }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"/api/flights/{Guid.NewGuid()}", ct)).StatusCode);

        var dashboardJson = JsonDocument.Parse(await dashboard.Content.ReadAsStringAsync(ct)).RootElement;
        AssertNoForbiddenKeys(dashboardJson);
        var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync(ct)).RootElement;
        var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync(ct)).RootElement;
        AssertNoForbiddenKeys(listJson);
        AssertNoForbiddenKeys(detailJson);
        var publicFlight = Assert.Single(detailJson.GetProperty("flights").EnumerateArray());
        Assert.Equal("Copa Airlines", publicFlight.GetProperty("airline").GetString());
        Assert.Equal("Confirmed", publicFlight.GetProperty("ticketStatus").GetString());
        Assert.True(publicFlight.GetProperty("hasTicketAccess").GetBoolean());
        Assert.StartsWith("/ticket/", publicFlight.GetProperty("ticketAccessPath").GetString());
        Assert.DoesNotContain("PRIVATE-PNR-999", detailJson.GetRawText());
        Assert.Equal("Copa Airlines", dashboardJson.GetProperty("airlines").EnumerateArray()
            .Single(x => x.GetProperty("name").GetString() == "Copa Airlines").GetProperty("name").GetString());
        var roomKpi = dashboardJson.GetProperty("kpis").EnumerateArray().Single(x => x.GetProperty("key").GetString() == "roomsConfirmed");
        Assert.Equal(0, roomKpi.GetProperty("value").GetInt32());
        Assert.Single(dashboardJson.GetProperty("alerts").EnumerateArray(), x => x.GetString() == "Transfer grupal pendiente");
    }

    [Fact]
    public async Task Opaque_ticket_redirect_enforces_status_headers_and_rate_limit()
    {
        await using var factory = new TravelControlWebFactory();
        var ct = TestContext.Current.CancellationToken;
        await SeedPassengerAsync(factory.Services, ct);
        string token;
        await using (var scope = factory.Services.CreateAsyncScope())
            token = await scope.ServiceProvider.GetRequiredService<AppDbContext>().PassengerFlights
                .Select(x => x.PublicTicketAccessToken).SingleAsync(ct);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            { AllowAutoRedirect = false });

        var valid = await client.GetAsync($"/ticket/{token}", ct);
        Assert.Equal(HttpStatusCode.Redirect, valid.StatusCode);
        Assert.True(valid.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", valid.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("mytrips.copaair.com", valid.Headers.Location?.Host);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/ticket/{Guid.NewGuid():N}", ct)).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.PassengerFlights.SingleAsync(ct);
            link.TicketAccessStatus = TicketAccessStatus.Invalid;
            await db.SaveChangesAsync(ct);
        }
        Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync($"/ticket/{token}", ct)).StatusCode);

        HttpResponseMessage? limited = null;
        for (var index = 0; index < 121; index++)
            limited = await client.GetAsync($"/ticket/{Guid.NewGuid():N}", ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited!.StatusCode);
    }

    [Fact]
    public async Task Public_and_private_dashboards_share_progress_and_operational_counts()
    {
        await using var factory = new TravelControlWebFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(client, ct);
        var publicDashboard = await client.GetFromJsonAsync<JsonElement>("/api/public/dashboard", ct);
        var privateResponse = await client.GetAsync("/api/dashboard", ct);
        Assert.True(privateResponse.IsSuccessStatusCode, await privateResponse.Content.ReadAsStringAsync(ct));
        var privateDashboard = JsonDocument.Parse(await privateResponse.Content.ReadAsStringAsync(ct)).RootElement;
        Assert.Equal(publicDashboard.GetProperty("progressPercent").GetInt32(),
            privateDashboard.GetProperty("tripReadiness").GetProperty("progressPercent").GetInt32());
        foreach (var key in new[] { "accommodationPassengers", "roomsConfirmed", "flights", "baggage", "documentation", "passports" })
        {
            var publicKpi = publicDashboard.GetProperty("kpis").EnumerateArray().Single(x => x.GetProperty("key").GetString() == key);
            var privateKpi = privateDashboard.GetProperty("kpis").EnumerateArray().Single(x => x.GetProperty("key").GetString() == key);
            Assert.Equal(publicKpi.GetProperty("value").GetInt32(), privateKpi.GetProperty("value").GetInt32());
            Assert.Equal(publicKpi.GetProperty("total").GetInt32(), privateKpi.GetProperty("total").GetInt32());
        }
        Assert.Equal(publicDashboard.GetProperty("missing").GetProperty("unresolvedRoomReservations").GetInt32(),
            privateDashboard.GetProperty("roomsPending").GetInt32());
        Assert.Equal(publicDashboard.GetProperty("missing").GetProperty("specificPropertiesPending").GetInt32(),
            privateDashboard.GetProperty("specificPropertiesPending").GetInt32());
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

    [Fact]
    public async Task Accommodation_kpis_count_46_people_and_25_distinct_rooms()
    {
        await using var factory = new TravelControlWebFactory();
        var ct = TestContext.Current.CancellationToken;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var trip = await db.Trips.SingleAsync(x => x.IsActive, ct);
            var op = await db.Operators.SingleAsync(x => x.Name == "Top Travel", ct);
            var passengerNumber = 0;
            for (var roomNumber = 1; roomNumber <= 25; roomNumber++)
            {
                var room = new RoomReservation
                {
                    TripId = trip.Id, OperatorId = op.Id, InternalCode = $"FIX-{roomNumber:00}",
                    Status = VerificationStatus.Confirmed, Hotel = "Hotel ficticio", RoomType = "Doble",
                    ExpectedCapacity = 2, CheckIn = trip.StartDate, CheckOut = trip.EndDate, SourceReference = "Referencia ficticia"
                };
                var occupants = roomNumber <= 21 ? 2 : 1;
                for (var index = 0; index < occupants; index++)
                {
                    passengerNumber++;
                    room.Passengers.Add(new Passenger
                    {
                        TripId = trip.Id, FullName = $"Persona {passengerNumber:00}", NormalizedName = $"PERSONA {passengerNumber:00}",
                        PrimaryOperatorId = op.Id
                    });
                }
                db.RoomReservations.Add(room);
            }
            await db.SaveChangesAsync(ct);
        }

        using var client = factory.CreateClient();
        var dashboard = await client.GetFromJsonAsync<JsonElement>("/api/public/dashboard", ct);
        var kpis = dashboard.GetProperty("kpis").EnumerateArray().ToArray();
        var accommodation = kpis.Single(x => x.GetProperty("key").GetString() == "accommodationPassengers");
        var rooms = kpis.Single(x => x.GetProperty("key").GetString() == "roomsConfirmed");
        Assert.Equal(46, accommodation.GetProperty("value").GetInt32());
        Assert.Equal(46, accommodation.GetProperty("total").GetInt32());
        Assert.Equal(25, rooms.GetProperty("value").GetInt32());
        Assert.Equal(25, rooms.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Operational_updated_at_tracks_room_flight_baggage_and_attachment_changes()
    {
        await using var factory = new TravelControlWebFactory();
        var ct = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<PublicReadService>();
        var trip = await db.Trips.SingleAsync(x => x.IsActive, ct);
        var op = await db.Operators.SingleAsync(x => x.Name == "Top Travel", ct);
        var room = new RoomReservation { TripId = trip.Id, OperatorId = op.Id, InternalCode = "UPDATED-ROOM", ExpectedCapacity = 1 };
        var passenger = new Passenger { TripId = trip.Id, FullName = "Persona actualizada", NormalizedName = "PERSONA ACTUALIZADA", RoomReservation = room };
        var flight = new FlightBooking { TripId = trip.Id, Pnr = "UPDATED-PNR" };
        flight.PassengerFlights.Add(new PassengerFlight { Passenger = passenger });
        var baggage = new BaggageEntitlement { Passenger = passenger, FlightBooking = flight };
        db.AddRange(room, passenger, flight, baggage);
        await db.SaveChangesAsync(ct);
        var previous = (await service.GetDashboardAsync(ct)).UpdatedAt;

        await Task.Delay(5, ct); room.Hotel = "Hotel nuevo"; await db.SaveChangesAsync(ct);
        var afterRoom = (await service.GetDashboardAsync(ct)).UpdatedAt; Assert.True(afterRoom > previous); previous = afterRoom;
        await Task.Delay(5, ct); flight.Airline = "Aerolínea nueva"; await db.SaveChangesAsync(ct);
        var afterFlight = (await service.GetDashboardAsync(ct)).UpdatedAt; Assert.True(afterFlight > previous); previous = afterFlight;
        await Task.Delay(5, ct); baggage.Notes = "Cambio ficticio"; await db.SaveChangesAsync(ct);
        var afterBaggage = (await service.GetDashboardAsync(ct)).UpdatedAt; Assert.True(afterBaggage > previous); previous = afterBaggage;
        await Task.Delay(5, ct);
        db.Attachments.Add(new Attachment
        {
            DocumentType = DocumentType.Other, OriginalName = "fixture.pdf", StoredName = "fixture.pdf",
            MimeType = "application/pdf", Size = 10, SecurePath = "fixture.pdf", Sha256 = "UPDATED-HASH", UploadedById = Guid.NewGuid(),
            Links = [new AttachmentLink { PassengerId = passenger.Id, EvidenceType = DocumentType.Other, CreatedByUserId = Guid.NewGuid() }]
        });
        await db.SaveChangesAsync(ct);
        Assert.True((await service.GetDashboardAsync(ct)).UpdatedAt > previous);
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
        var flight = new FlightBooking
        {
            TripId = trip.Id, Pnr = "PRIVATE-PNR-999", Airline = "Copa Airlines (CM)", Status = VerificationStatus.Confirmed
        };
        flight.PassengerFlights.Add(new PassengerFlight
        {
            Passenger = passenger, TicketStatus = VerificationStatus.Confirmed,
            ElectronicTicketNumber = "PRIVATE-ELECTRONIC-TICKET-999", BookingLookupLastName = "Fictional",
            TicketAccessStatus = TicketAccessStatus.Verified,
            TicketAccessUrl = "https://mytrips.copaair.com/trip-detail/PRIVATEPNR999/FICTIONAL",
            TicketAccessVerifiedAt = DateTimeOffset.UtcNow
        });
        db.AddRange(passenger, flight);
        await db.SaveChangesAsync(ct);
        return passenger.Id;
    }

    private static async Task AuthenticateAsync(HttpClient client, CancellationToken ct)
    {
        var csrfResponse = await client.GetAsync("/api/auth/csrf", ct);
        Assert.True(csrfResponse.IsSuccessStatusCode, await csrfResponse.Content.ReadAsStringAsync(ct));
        var csrf = JsonDocument.Parse(await csrfResponse.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("token").GetString()!;
        using var setupRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
        {
            Content = JsonContent.Create(new { email = Email, password = Password, displayName = "Administrador" })
        };
        setupRequest.Headers.Add("X-XSRF-TOKEN", csrf);
        var setup = await client.SendAsync(setupRequest, ct);
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = Email, password = Password, rememberMe = false })
        };
        loginRequest.Headers.Add("X-XSRF-TOKEN", csrf);
        var login = await client.SendAsync(loginRequest, ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
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
