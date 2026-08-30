using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;
using Xunit;

namespace TravelControl.Api.Tests;

public sealed class MutationIntegrityTests
{
    private const string Email = "admin@example.test";
    private const string Password = "Test-only-Password!2026";

    [Fact]
    public async Task Differential_flight_update_preserves_ticket_and_enforces_concurrency_and_removal_confirmation()
    {
        await using var factory = new TravelControlWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var ct = TestContext.Current.CancellationToken;
        var csrf = await AuthenticateAsync(client, ct);
        var fixture = await SeedFlightAsync(factory.Services, ct);
        var addedPassengerId = await SeedPassengerAsync(factory.Services, "Persona agregada", ct);
        var request = new
        {
            status = "ToVerify", airline = "Aerolínea ficticia actualizada", issuingAgency = (string?)null, pnr = "PNR-FIXTURE",
            generalReference = (string?)null, sourceReference = (string?)null, notes = "Cambio ficticio",
            passengerIds = new[] { fixture.PassengerId, addedPassengerId }, version = fixture.Version,
            segments = fixture.Segments.Select(x => new { id = (Guid?)x.Id, type = x.Type.ToString(), flightNumber = x.FlightNumber,
                originAirport = x.OriginAirport, destinationAirport = x.DestinationAirport, departureAt = x.DepartureAt,
                arrivalAt = x.ArrivalAt, originTimeZone = (string?)null, destinationTimeZone = (string?)null, sequence = x.Sequence })
        };
        var updated = await SendJsonAsync(client, HttpMethod.Put, $"/api/flights/{fixture.FlightId}", request, csrf);
        Assert.True(updated.StatusCode == HttpStatusCode.NoContent, await updated.Content.ReadAsStringAsync(ct));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var links = await scope.ServiceProvider.GetRequiredService<AppDbContext>().PassengerFlights.ToListAsync(ct);
            var link = Assert.Single(links, x => x.PassengerId == fixture.PassengerId);
            Assert.Equal("TICKET-FIXTURE-001", link.ElectronicTicketNumber);
            Assert.Equal(VerificationStatus.Confirmed, link.TicketStatus);
            Assert.Equal("Nota ficticia conservada", link.Notes);
            var added = Assert.Single(links, x => x.PassengerId == addedPassengerId);
            Assert.Null(added.ElectronicTicketNumber);
            Assert.Equal(VerificationStatus.ToVerify, added.TicketStatus);
            Assert.Equal(1, added.Version);
            Assert.Equal(VerificationStatus.InProgress, await scope.ServiceProvider.GetRequiredService<AppDbContext>()
                .FlightBookings.Where(x => x.Id == fixture.FlightId).Select(x => x.Status).SingleAsync(ct));
        }
        Assert.Equal(HttpStatusCode.Conflict, (await SendJsonAsync(client, HttpMethod.Put, $"/api/flights/{fixture.FlightId}", request, csrf)).StatusCode);

        var currentVersion = await CurrentVersionAsync(factory.Services, fixture.FlightId, ct);
        var removal = new
        {
            status = "ToVerify", airline = "Aerolínea ficticia", issuingAgency = (string?)null, pnr = "PNR-FIXTURE",
            generalReference = (string?)null, sourceReference = (string?)null, notes = (string?)null,
            passengerIds = Array.Empty<Guid>(), version = currentVersion, segments = request.segments
        };
        Assert.Equal(HttpStatusCode.Conflict, (await SendJsonAsync(client, HttpMethod.Put, $"/api/flights/{fixture.FlightId}", removal, csrf)).StatusCode);
    }

    [Fact]
    public async Task Ticket_and_baggage_confirmations_enforce_effective_rules_and_group_reports_skips()
    {
        await using var factory = new TravelControlWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var ct = TestContext.Current.CancellationToken;
        var csrf = await AuthenticateAsync(client, ct);
        var fixture = await SeedBaggageFixtureAsync(factory.Services, ct);
        var ticketVersion = await CurrentTicketVersionAsync(factory.Services, fixture.FlightId, fixture.EligiblePassengerId, ct);

        var incompleteTicket = await SendJsonAsync(client, HttpMethod.Put,
            $"/api/flights/{fixture.FlightId}/passengers/{fixture.EligiblePassengerId}/ticket",
            new { electronicTicketNumber = "", status = "Confirmed", notes = (string?)null, version = ticketVersion }, csrf);
        Assert.Equal(HttpStatusCode.BadRequest, incompleteTicket.StatusCode);
        Assert.Contains("ticket electrónico", await incompleteTicket.Content.ReadAsStringAsync(ct), StringComparison.OrdinalIgnoreCase);

        var withoutBooking = Baggage(fixture.EligiblePassengerId, null, 1, 23, true, true, null);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendJsonAsync(client, HttpMethod.Post, "/api/baggage", withoutBooking, csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendJsonAsync(client, HttpMethod.Post, "/api/baggage",
            Baggage(fixture.EligiblePassengerId, fixture.FlightId, 1, 22, true, true, null), csrf)).StatusCode);
        var withoutReturn = await SendJsonAsync(client, HttpMethod.Post, "/api/baggage",
            Baggage(fixture.EligiblePassengerId, fixture.FlightId, 1, 23, true, false, null), csrf);
        Assert.True(withoutReturn.StatusCode == HttpStatusCode.BadRequest, await withoutReturn.Content.ReadAsStringAsync(ct));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendJsonAsync(client, HttpMethod.Post, "/api/baggage",
            Baggage(fixture.PendingPassengerId, fixture.FlightId, 1, 23, true, true, null), csrf)).StatusCode);

        var justified = await SendJsonAsync(client, HttpMethod.Post, "/api/baggage",
            Baggage(fixture.EligiblePassengerId, fixture.FlightId, 1, 23, true, false, "Excepción ficticia documentada"), csrf);
        Assert.Equal(HttpStatusCode.Created, justified.StatusCode);
        var createdBaggage = JsonDocument.Parse(await justified.Content.ReadAsStringAsync(ct)).RootElement;
        var baggageId = createdBaggage.GetProperty("id").GetGuid();
        var baggageVersion = createdBaggage.GetProperty("version").GetInt64();
        var concurrentUpdate = new
        {
            passengerId = fixture.EligiblePassengerId, flightBookingId = fixture.FlightId, status = "Confirmed",
            checkedBagCount = 1, weightPerBagKg = 23, appliesOutbound = true, appliesReturn = false,
            exceptionReason = "Excepción ficticia actualizada", sourceReference = "Comprobante ficticio", notes = "Cambio A",
            version = baggageVersion
        };
        Assert.Equal(HttpStatusCode.NoContent, (await SendJsonAsync(client, HttpMethod.Put, $"/api/baggage/{baggageId}", concurrentUpdate, csrf)).StatusCode);
        var stale = await SendJsonAsync(client, HttpMethod.Put, $"/api/baggage/{baggageId}", concurrentUpdate, csrf);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("recargá la vista", await stale.Content.ReadAsStringAsync(ct));
        Assert.Equal(HttpStatusCode.Conflict, (await SendJsonAsync(client, HttpMethod.Post, "/api/baggage",
            Baggage(fixture.EligiblePassengerId, fixture.FlightId, 1, 23, true, true, null), csrf)).StatusCode);

        var group = await SendJsonAsync(client, HttpMethod.Post, "/api/baggage/confirm-group",
            new { flightBookingId = fixture.FlightId, passengerIds = new[] { fixture.EligiblePassengerId, fixture.PendingPassengerId }, sourceReference = "Comprobante ficticio", notes = (string?)null }, csrf);
        Assert.Equal(HttpStatusCode.OK, group.StatusCode);
        var result = JsonDocument.Parse(await group.Content.ReadAsStringAsync(ct)).RootElement;
        Assert.Equal(1, result.GetProperty("updated").GetInt32());
        var skipped = Assert.Single(result.GetProperty("skipped").EnumerateArray());
        Assert.Equal(fixture.PendingPassengerId, skipped.GetProperty("passengerId").GetGuid());
        Assert.Contains("Ticket todavía no confirmado", skipped.GetProperty("reason").GetString());
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().BaggageEntitlements
            .CountAsync(x => x.PassengerId == fixture.EligiblePassengerId && x.FlightBookingId == fixture.FlightId, ct));
    }

    [Fact]
    public async Task Ticket_updates_use_optimistic_concurrency_mask_audit_values_and_recalculate_pnr_status()
    {
        await using var factory = new TravelControlWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var ct = TestContext.Current.CancellationToken;
        var csrf = await AuthenticateAsync(client, ct);
        var fixture = await SeedBaggageFixtureAsync(factory.Services, ct);
        var version = await CurrentTicketVersionAsync(factory.Services, fixture.FlightId, fixture.PendingPassengerId, ct);
        var payload = new { electronicTicketNumber = "SECRET-TICKET-987654", status = "Confirmed", notes = "fixture", version };

        var flightsResponse = await client.GetAsync("/api/flights", ct);
        Assert.True(flightsResponse.IsSuccessStatusCode, await flightsResponse.Content.ReadAsStringAsync(ct));
        _ = JsonDocument.Parse(await flightsResponse.Content.ReadAsStringAsync(ct));

        var first = await SendJsonAsync(client, HttpMethod.Put,
            $"/api/flights/{fixture.FlightId}/passengers/{fixture.PendingPassengerId}/ticket", payload, csrf);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var stale = await SendJsonAsync(client, HttpMethod.Put,
            $"/api/flights/{fixture.FlightId}/passengers/{fixture.PendingPassengerId}/ticket", payload, csrf);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("Recarg", await stale.Content.ReadAsStringAsync(ct), StringComparison.OrdinalIgnoreCase);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(VerificationStatus.Confirmed, await db.FlightBookings.Where(x => x.Id == fixture.FlightId).Select(x => x.Status).SingleAsync(ct));
        var audit = (await db.AuditLogs.Where(x => x.EntityName == "PassengerFlight").ToListAsync(ct)).OrderByDescending(x => x.At).First();
        Assert.DoesNotContain("SECRET-TICKET-987654", audit.NewValue);
        Assert.Contains("654", audit.NewValue);
    }

    [Fact]
    public async Task Last_administrator_and_own_account_are_protected_and_attempts_are_audited()
    {
        await using var factory = new TravelControlWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var ct = TestContext.Current.CancellationToken;
        var csrf = await AuthenticateAsync(client, ct);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me", ct);
        var id = me.GetProperty("id").GetGuid();
        var selfDisable = await SendJsonAsync(client, HttpMethod.Put, $"/api/users/{id}", new { displayName = "Administrador", role = "Administrator", isActive = false }, csrf);
        Assert.True(selfDisable.StatusCode == HttpStatusCode.BadRequest, await selfDisable.Content.ReadAsStringAsync(ct));
        var downgrade = await SendJsonAsync(client, HttpMethod.Put, $"/api/users/{id}", new { displayName = "Administrador", role = "Viewer", isActive = true }, csrf);
        Assert.Equal(HttpStatusCode.BadRequest, downgrade.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var blocked = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs.CountAsync(x => x.Action == "ProtectionBlocked", ct);
        Assert.Equal(2, blocked);
    }

    [Fact]
    public async Task Viewer_can_read_attachments_but_cannot_modify_links_or_files()
    {
        await using var factory = new TravelControlWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var ct = TestContext.Current.CancellationToken;
        var adminCsrf = await AuthenticateAsync(client, ct);
        var createViewer = await SendJsonAsync(client, HttpMethod.Post, "/api/users", new
        {
            email = "viewer@example.test", displayName = "Viewer", role = "Viewer", initialPassword = "Viewer-only-Password!2026"
        }, adminCsrf);
        Assert.Equal(HttpStatusCode.Created, createViewer.StatusCode);
        var fixture = await SeedAttachmentAsync(factory.Services, ct);

        var logout = await SendJsonAsync(client, HttpMethod.Post, "/api/auth/logout", new { }, adminCsrf);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var anonymousCsrfResponse = await client.GetAsync("/api/auth/csrf", ct);
        var anonymousCsrf = JsonDocument.Parse(await anonymousCsrfResponse.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("token").GetString()!;
        var login = await SendJsonAsync(client, HttpMethod.Post, "/api/auth/login", new
        {
            email = "viewer@example.test", password = "Viewer-only-Password!2026", rememberMe = false
        }, anonymousCsrf);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var viewerCsrfResponse = await client.GetAsync("/api/auth/csrf", ct);
        var viewerCsrf = JsonDocument.Parse(await viewerCsrfResponse.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("token").GetString()!;

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/attachments?passengerId={fixture.PassengerId}", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/attachments/{fixture.AttachmentId}", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendJsonAsync(client, HttpMethod.Post, $"/api/attachments/{fixture.AttachmentId}/links",
            new { passengerId = fixture.PassengerId, roomId = (Guid?)null, flightId = (Guid?)null, baggageId = (Guid?)null, evidenceType = "Other" }, viewerCsrf)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendJsonAsync(client, HttpMethod.Delete,
            $"/api/attachments/{fixture.AttachmentId}/links/{fixture.LinkId}", new { }, viewerCsrf)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendJsonAsync(client, HttpMethod.Delete,
            $"/api/attachments/{fixture.AttachmentId}/links/{fixture.LinkId}?deleteIfOrphan=true", new { }, viewerCsrf)).StatusCode);
    }

    [Fact]
    public async Task Editor_can_unlink_but_only_administrator_can_request_physical_orphan_deletion()
    {
        await using var factory = new TravelControlWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var ct = TestContext.Current.CancellationToken;
        var adminCsrf = await AuthenticateAsync(client, ct);
        var createEditor = await SendJsonAsync(client, HttpMethod.Post, "/api/users", new
        {
            email = "editor@example.test", displayName = "Editor", role = "Editor", initialPassword = "Editor-only-Password!2026"
        }, adminCsrf);
        Assert.Equal(HttpStatusCode.Created, createEditor.StatusCode);
        var fixture = await SeedAttachmentAsync(factory.Services, ct);
        Assert.Equal(HttpStatusCode.NoContent, (await SendJsonAsync(client, HttpMethod.Post, "/api/auth/logout", new { }, adminCsrf)).StatusCode);
        var anonymousCsrfResponse = await client.GetAsync("/api/auth/csrf", ct);
        var anonymousCsrf = JsonDocument.Parse(await anonymousCsrfResponse.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("token").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(client, HttpMethod.Post, "/api/auth/login", new
        {
            email = "editor@example.test", password = "Editor-only-Password!2026", rememberMe = false
        }, anonymousCsrf)).StatusCode);
        var editorCsrfResponse = await client.GetAsync("/api/auth/csrf", ct);
        var editorCsrf = JsonDocument.Parse(await editorCsrfResponse.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("token").GetString()!;

        Assert.Equal(HttpStatusCode.Forbidden, (await SendJsonAsync(client, HttpMethod.Delete,
            $"/api/attachments/{fixture.AttachmentId}/links/{fixture.LinkId}?deleteIfOrphan=true", new { }, editorCsrf)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(client, HttpMethod.Delete,
            $"/api/attachments/{fixture.AttachmentId}/links/{fixture.LinkId}?deleteIfOrphan=false", new { }, editorCsrf)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Attachments.CountAsync(x => x.Id == fixture.AttachmentId, ct));
        Assert.Equal(0, await db.AttachmentLinks.CountAsync(x => x.AttachmentId == fixture.AttachmentId, ct));
    }

    private static async Task<string> AuthenticateAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetAsync("/api/auth/csrf", ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(ct));
        var csrf = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("token").GetString()!;
        var setup = await SendJsonAsync(client, HttpMethod.Post, "/api/auth/setup", new { email = Email, password = Password, displayName = "Administrador" }, csrf);
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);
        var login = await SendJsonAsync(client, HttpMethod.Post, "/api/auth/login", new { email = Email, password = Password, rememberMe = false }, csrf);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var authenticatedCsrf = await client.GetAsync("/api/auth/csrf", ct);
        Assert.True(authenticatedCsrf.IsSuccessStatusCode, await authenticatedCsrf.Content.ReadAsStringAsync(ct));
        return JsonDocument.Parse(await authenticatedCsrf.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(HttpClient client, HttpMethod method, string path, object value, string csrf)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(value) };
        request.Headers.Add("X-XSRF-TOKEN", csrf);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<(Guid FlightId, Guid PassengerId, long Version, FlightSegment[] Segments)> SeedFlightAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trip = await db.Trips.SingleAsync(x => x.IsActive, ct);
        var passenger = new Passenger { TripId = trip.Id, FullName = "Persona de vuelo", NormalizedName = "PERSONA DE VUELO" };
        var flight = new FlightBooking
        {
            TripId = trip.Id, Airline = "Aerolínea ficticia", Pnr = "PNR-FIXTURE", Status = VerificationStatus.ToVerify,
            Segments =
            [
                new FlightSegment { Type = SegmentType.Outbound, FlightNumber = "FX1", OriginAirport = "AAA", DestinationAirport = "BBB", DepartureAt = DateTimeOffset.UtcNow.AddDays(1), ArrivalAt = DateTimeOffset.UtcNow.AddDays(1).AddHours(2), Sequence = 1 },
                new FlightSegment { Type = SegmentType.Return, FlightNumber = "FX2", OriginAirport = "BBB", DestinationAirport = "AAA", DepartureAt = DateTimeOffset.UtcNow.AddDays(5), ArrivalAt = DateTimeOffset.UtcNow.AddDays(5).AddHours(2), Sequence = 2 }
            ]
        };
        flight.PassengerFlights.Add(new PassengerFlight { Passenger = passenger, ElectronicTicketNumber = "TICKET-FIXTURE-001", TicketStatus = VerificationStatus.Confirmed, Notes = "Nota ficticia conservada" });
        db.FlightBookings.Add(flight);
        await db.SaveChangesAsync(ct);
        return (flight.Id, passenger.Id, flight.Version, flight.Segments.ToArray());
    }

    private static async Task<long> CurrentVersionAsync(IServiceProvider services, Guid flightId, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().FlightBookings.Where(x => x.Id == flightId).Select(x => x.Version).SingleAsync(ct);
    }

    private static async Task<long> CurrentTicketVersionAsync(IServiceProvider services, Guid flightId, Guid passengerId, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().PassengerFlights
            .Where(x => x.FlightBookingId == flightId && x.PassengerId == passengerId)
            .Select(x => x.Version).SingleAsync(ct);
    }

    private static object Baggage(Guid passengerId, Guid? flightId, int count, decimal weight, bool outbound, bool inbound, string? exception) => new
    {
        passengerId, flightBookingId = flightId, status = "Confirmed", checkedBagCount = count, weightPerBagKg = weight,
        appliesOutbound = outbound, appliesReturn = inbound, exceptionReason = exception, sourceReference = "Comprobante ficticio", notes = (string?)null
    };

    private static async Task<Guid> SeedPassengerAsync(IServiceProvider services, string name, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tripId = await db.Trips.Where(x => x.IsActive).Select(x => x.Id).SingleAsync(ct);
        var passenger = new Passenger { TripId = tripId, FullName = name, NormalizedName = name.ToUpperInvariant() };
        db.Passengers.Add(passenger);
        await db.SaveChangesAsync(ct);
        return passenger.Id;
    }

    private static async Task<(Guid FlightId, Guid EligiblePassengerId, Guid PendingPassengerId)> SeedBaggageFixtureAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trip = await db.Trips.SingleAsync(x => x.IsActive, ct);
        var eligible = new Passenger { TripId = trip.Id, FullName = "Persona elegible", NormalizedName = "PERSONA ELEGIBLE" };
        var pending = new Passenger { TripId = trip.Id, FullName = "Persona pendiente", NormalizedName = "PERSONA PENDIENTE" };
        var booking = new FlightBooking
        {
            TripId = trip.Id, Airline = "Aerolínea ficticia", Pnr = "PNR-BAGGAGE", Status = VerificationStatus.Confirmed,
            Segments =
            [
                new FlightSegment { Type = SegmentType.Outbound, FlightNumber = "FX10", OriginAirport = "AAA", DestinationAirport = "BBB", DepartureAt = DateTimeOffset.UtcNow.AddDays(1), ArrivalAt = DateTimeOffset.UtcNow.AddDays(1).AddHours(2), Sequence = 1 },
                new FlightSegment { Type = SegmentType.Return, FlightNumber = "FX20", OriginAirport = "BBB", DestinationAirport = "AAA", DepartureAt = DateTimeOffset.UtcNow.AddDays(5), ArrivalAt = DateTimeOffset.UtcNow.AddDays(5).AddHours(2), Sequence = 2 }
            ],
            PassengerFlights =
            [
                new PassengerFlight { Passenger = eligible, ElectronicTicketNumber = "TICKET-ELIGIBLE", TicketStatus = VerificationStatus.Confirmed },
                new PassengerFlight { Passenger = pending, ElectronicTicketNumber = "TICKET-PENDING", TicketStatus = VerificationStatus.ToVerify }
            ]
        };
        db.FlightBookings.Add(booking);
        await db.SaveChangesAsync(ct);
        return (booking.Id, eligible.Id, pending.Id);
    }

    private static async Task<(Guid AttachmentId, Guid LinkId, Guid PassengerId)> SeedAttachmentAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tripId = await db.Trips.Where(x => x.IsActive).Select(x => x.Id).SingleAsync(ct);
        var passenger = new Passenger { TripId = tripId, FullName = "Persona con adjunto", NormalizedName = "PERSONA CON ADJUNTO" };
        var link = new AttachmentLink { Passenger = passenger, CreatedByUserId = Guid.NewGuid() };
        var storageRoot = scope.ServiceProvider.GetRequiredService<IConfiguration>()["Storage:Root"]!;
        var path = Path.Combine(storageRoot, $"{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(path, "%PDF-1.7\nfixture"u8.ToArray(), ct);
        var attachment = new Attachment
        {
            DocumentType = DocumentType.Other, OriginalName = "fixture.pdf", StoredName = Path.GetFileName(path),
            MimeType = "application/pdf", Size = new FileInfo(path).Length, SecurePath = path, Sha256 = $"HASH-{Guid.NewGuid():N}",
            UploadedById = Guid.NewGuid(), Links = [link]
        };
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        return (attachment.Id, link.Id, passenger.Id);
    }
}
