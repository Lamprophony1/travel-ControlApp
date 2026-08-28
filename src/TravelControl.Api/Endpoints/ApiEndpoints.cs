using FluentValidation;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using TravelControl.Application.Contracts;
using TravelControl.Application.Services;
using TravelControl.Domain;
using TravelControl.Infrastructure.Identity;
using TravelControl.Infrastructure.Persistence;
using TravelControl.Infrastructure.Services;

namespace TravelControl.Api.Endpoints;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapTravelControlApi(this IEndpointRouteBuilder endpoints)
    {
        MapAuth(endpoints);
        var api = endpoints.MapGroup("/api").RequireAuthorization();
        MapDashboard(api); MapPassengers(api); MapRooms(api); MapFlights(api); MapBaggage(api);
        MapTransfer(api); MapFollowUps(api); MapImportsExports(api); MapAttachments(api);
        MapUsers(api); MapReference(api); MapAudit(api);
        return endpoints;
    }

    private static void MapAuth(IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/auth");
        auth.MapGet("/csrf", (IAntiforgery antiforgery, HttpContext ctx) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(ctx).RequestToken })).AllowAnonymous();
        auth.MapGet("/setup-status", async (UserManager<AppUser> users) =>
            Results.Ok(new { required = !await users.Users.AnyAsync() })).AllowAnonymous();
        auth.MapPost("/setup", async (SetupRequest request, UserManager<AppUser> users) =>
        {
            if (await users.Users.AnyAsync()) return Results.Conflict(new { message = "La configuración inicial ya fue completada." });
            var user = new AppUser { UserName = request.Email.Trim(), Email = request.Email.Trim(), DisplayName = request.DisplayName.Trim(), EmailConfirmed = true };
            var result = await users.CreateAsync(user, request.Password);
            if (!result.Succeeded) return IdentityErrors(result);
            await users.AddToRoleAsync(user, nameof(UserRole.Administrator));
            return Results.Created("/api/auth/me", new { message = "Administrador creado." });
        }).AllowAnonymous().RequireRateLimiting("auth");
        auth.MapPost("/login", async (LoginRequest request, SignInManager<AppUser> signIn, UserManager<AppUser> users) =>
        {
            var user = await users.FindByEmailAsync(request.Email.Trim());
            if (user is null || !user.IsActive) return Results.Problem("Correo o contraseña incorrectos.", statusCode: 401);
            var result = await signIn.PasswordSignInAsync(user, request.Password, request.RememberMe, lockoutOnFailure: true);
            return result.Succeeded ? Results.Ok(new { message = "Sesión iniciada." })
                : result.IsLockedOut ? Results.Problem("Cuenta bloqueada temporalmente.", statusCode: 423)
                : Results.Problem("Correo o contraseña incorrectos.", statusCode: 401);
        }).AllowAnonymous().RequireRateLimiting("auth");
        auth.MapPost("/logout", async (SignInManager<AppUser> signIn) => { await signIn.SignOutAsync(); return Results.NoContent(); }).RequireAuthorization();
        auth.MapGet("/me", async (ClaimsPrincipal principal, UserManager<AppUser> users) =>
        {
            var user = await users.GetUserAsync(principal); if (user is null || !user.IsActive) return Results.Unauthorized();
            return Results.Ok(new { user.Id, user.Email, user.DisplayName, roles = await users.GetRolesAsync(user) });
        }).RequireAuthorization();
    }

    private static void MapDashboard(RouteGroupBuilder api) => api.MapGet("/dashboard", async (
        string? operatorName, string? overall, DashboardService service, CancellationToken ct) =>
        Results.Ok(await service.GetAsync(operatorName, overall, ct)));

    private static void MapPassengers(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/passengers");
        group.MapGet("/", async (string? search, string? operatorName, string? overall, string? requirement, string? status,
            int page, int pageSize, PassengerQueryService service, CancellationToken ct) =>
        {
            page = Math.Max(1, page); pageSize = Math.Clamp(pageSize == 0 ? 25 : pageSize, 1, 100);
            var query = service.BaseQuery();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var normalized = TextNormalizer.Normalize(search);
                query = query.Where(x => x.NormalizedName.Contains(normalized)
                    || (x.NormalizedPassportNumber != null && x.NormalizedPassportNumber.Contains(normalized))
                    || (x.RoomReservation != null && x.RoomReservation.InternalCode.Contains(search))
                    || x.PassengerFlights.Any(f => (f.FlightBooking.Pnr != null && f.FlightBooking.Pnr.Contains(search))
                        || (f.ElectronicTicketNumber != null && f.ElectronicTicketNumber.Contains(search))));
            }
            if (!string.IsNullOrWhiteSpace(operatorName)) query = query.Where(x => x.PrimaryOperator != null && x.PrimaryOperator.Name == operatorName);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var mapped = (await query.OrderBy(x => x.FullName).ToListAsync(ct)).Select(x => PassengerQueryService.Map(x, today));
            if (Enum.TryParse<PassengerOverallStatus>(overall, true, out var overallValue)) mapped = mapped.Where(x => x.OverallStatus == overallValue);
            if (!string.IsNullOrWhiteSpace(requirement)) mapped = mapped.Where(x =>
            {
                var item = x.Requirements.FirstOrDefault(r => r.Key == requirement);
                return item is not null && (!Enum.TryParse<VerificationStatus>(status, true, out var statusValue)
                    ? !BusinessRules.IsResolved(item) : item.Status == statusValue);
            });
            var result = mapped.ToList();
            return Results.Ok(new PagedResult<PassengerListItem>(result.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, result.Count));
        });
        group.MapGet("/{id:guid}", async (Guid id, PassengerQueryService service, CancellationToken ct) =>
        {
            var p = await service.BaseQuery().SingleOrDefaultAsync(x => x.Id == id, ct); if (p is null) return Results.NotFound();
            return Results.Ok(new
            {
                passenger = new { p.Id, p.FullName, p.BirthDate, p.Nationality, p.PassportExpiry, p.Phone, p.Email, p.EstimatedHotelArrival,
                    p.DietaryRestrictions, p.Notes, p.NextAction, p.NextActionDueDate, p.PassportReviewStatus, p.DocumentationStatus,
                    p.DocumentationExceptionReason, p.Version,
                    PrimaryOperator = p.PrimaryOperator is null ? null : new { p.PrimaryOperator.Id, p.PrimaryOperator.Name },
                    RoomReservation = p.RoomReservation is null ? null : new { p.RoomReservation.Id, p.RoomReservation.InternalCode, p.RoomReservation.Hotel,
                        p.RoomReservation.RoomType, p.RoomReservation.CheckIn, p.RoomReservation.CheckOut, p.RoomReservation.Status },
                    PassengerFlights = p.PassengerFlights.Select(x => new { x.FlightBookingId, x.ElectronicTicketNumber, x.TicketStatus, x.Notes,
                        Booking = new { x.FlightBooking.Pnr, x.FlightBooking.Airline, x.FlightBooking.Status,
                            Segments = x.FlightBooking.Segments.OrderBy(s => s.Sequence).Select(s => new { s.Id, s.Type, s.FlightNumber, s.OriginAirport, s.DestinationAirport, s.DepartureAt, s.ArrivalAt }) } }),
                    BaggageEntitlements = p.BaggageEntitlements.Select(x => new { x.Id, x.FlightBookingId, x.Status, x.CheckedBagCount,
                        x.WeightPerBagKg, x.Includes23Kg, x.AppliesOutbound, x.AppliesReturn, x.ExceptionReason, x.SourceReference, x.Notes }),
                    FollowUps = p.FollowUps.Select(x => new { x.Id, x.Title, x.Description, x.DueDate, x.Status, x.Priority }) },
                computed = BusinessRules.CalculatePassenger(p, DateOnly.FromDateTime(DateTime.UtcNow)),
                maskedPassport = PassengerQueryService.MaskPassport(p.PassportNumber)
            });
        });
        group.MapGet("/{id:guid}/passport", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var value = await db.Passengers.Where(x => x.Id == id).Select(x => x.PassportNumber).SingleOrDefaultAsync(ct);
            return value is null ? Results.NotFound() : Results.Ok(new { passportNumber = value });
        }).RequireAuthorization("CanEdit");
        group.MapPost("/", async (CreatePassengerRequest req, IValidator<CreatePassengerRequest> validator, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct); if (!validation.IsValid) return Validation(validation);
            var trip = await db.Trips.SingleAsync(x => x.IsActive, ct); var normalized = TextNormalizer.Normalize(req.FullName);
            if (await db.Passengers.AnyAsync(x => x.TripId == trip.Id && x.NormalizedName == normalized, ct)) return Results.Conflict(new { message = "Ya existe un pasajero con ese nombre." });
            var entity = new Passenger { TripId = trip.Id, FullName = req.FullName.Trim(), NormalizedName = normalized, BirthDate = req.BirthDate,
                Nationality = req.Nationality, PassportNumber = Blank(req.PassportNumber), NormalizedPassportNumber = Blank(TextNormalizer.Normalize(req.PassportNumber)),
                PassportExpiry = req.PassportExpiry, Phone = req.Phone, Email = req.Email, PrimaryOperatorId = req.PrimaryOperatorId,
                RoomReservationId = req.RoomReservationId, NextAction = req.NextAction, NextActionDueDate = req.NextActionDueDate,
                DietaryRestrictions = req.DietaryRestrictions, Notes = req.Notes, CreatedById = UserId(user), UpdatedById = UserId(user) };
            db.Passengers.Add(entity); await db.SaveChangesAsync(ct); await Audit(db, user, "Passenger", entity.Id, entity.Id, "Create", null, new { entity.FullName }, ct);
            return Results.Created($"/api/passengers/{entity.Id}", new { entity.Id });
        }).RequireAuthorization("CanEdit");
        group.MapPut("/{id:guid}", async (Guid id, UpdatePassengerRequest req, IValidator<UpdatePassengerRequest> validator, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct); if (!validation.IsValid) return Validation(validation);
            var entity = await db.Passengers.FindAsync([id], ct); if (entity is null) return Results.NotFound();
            if (entity.Version != req.Version) return Conflict();
            if (req.DocumentationStatus == VerificationStatus.Confirmed)
            {
                var missing = new List<string>();
                if (req.PassportReviewStatus != VerificationStatus.Confirmed) missing.Add("pasaporte revisado");
                if (!await db.PassengerFlights.AnyAsync(x => x.PassengerId == id && x.TicketStatus == VerificationStatus.Confirmed, ct)) missing.Add("ticket revisado");
                if (!(req.RoomReservationId.HasValue && await db.RoomReservations.AnyAsync(x => x.Id == req.RoomReservationId && x.Status == VerificationStatus.Confirmed && x.SourceReference != null, ct))) missing.Add("voucher o referencia de habitación");
                if (!await db.PassengerFlights.AnyAsync(x => x.PassengerId == id && x.FlightBooking.SourceReference != null, ct)
                    && !await db.Attachments.AnyAsync(x => x.PassengerId == id && x.DocumentType == DocumentType.AirTicket, ct)) missing.Add("comprobante de vuelo");
                if (missing.Count > 0) return Results.BadRequest(new { message = "No se puede confirmar la documentación.", missing });
            }
            var before = new { entity.FullName, passport = PassengerQueryService.MaskPassport(entity.PassportNumber), entity.DocumentationStatus, entity.RoomReservationId, entity.NextAction };
            entity.FullName = req.FullName.Trim(); entity.NormalizedName = TextNormalizer.Normalize(req.FullName); entity.BirthDate = req.BirthDate; entity.Nationality = req.Nationality;
            entity.PassportNumber = Blank(req.PassportNumber); entity.NormalizedPassportNumber = Blank(TextNormalizer.Normalize(req.PassportNumber)); entity.PassportExpiry = req.PassportExpiry;
            entity.PassportReviewStatus = req.PassportReviewStatus; entity.DocumentationStatus = req.DocumentationStatus; entity.DocumentationExceptionReason = req.DocumentationExceptionReason;
            if (req.DocumentationStatus == VerificationStatus.Confirmed) { entity.DocumentationVerifiedAt = DateTimeOffset.UtcNow; entity.DocumentationVerifiedById = UserId(user); }
            entity.Phone = req.Phone; entity.Email = req.Email; entity.PrimaryOperatorId = req.PrimaryOperatorId; entity.RoomReservationId = req.RoomReservationId;
            entity.EstimatedHotelArrival = req.EstimatedHotelArrival; entity.DietaryRestrictions = req.DietaryRestrictions; entity.Notes = req.Notes;
            entity.NextAction = req.NextAction; entity.NextActionDueDate = req.NextActionDueDate; entity.UpdatedById = UserId(user);
            try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Conflict(); }
            await Audit(db, user, "Passenger", id, id, "Update", before, new { entity.FullName, passport = PassengerQueryService.MaskPassport(entity.PassportNumber), entity.DocumentationStatus, entity.RoomReservationId, entity.NextAction }, ct);
            return Results.NoContent();
        }).RequireAuthorization("CanEdit");
        group.MapPost("/bulk-assign", async (BulkAssignRequest req, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (req.PassengerIds.Count == 0) return Results.BadRequest(new { message = "Seleccioná al menos un pasajero." });
            var entities = await db.Passengers.Where(x => req.PassengerIds.Contains(x.Id)).ToListAsync(ct);
            var existingFlights = req.FlightBookingId.HasValue ? await db.PassengerFlights.Where(x => req.PassengerIds.Contains(x.PassengerId) && x.FlightBookingId == req.FlightBookingId).Select(x => x.PassengerId).ToListAsync(ct) : [];
            foreach (var entity in entities)
            {
                if (req.RoomReservationId.HasValue) entity.RoomReservationId = req.RoomReservationId;
                if (req.NextAction is not null) { entity.NextAction = req.NextAction; entity.NextActionDueDate = req.NextActionDueDate; }
                if (req.FlightBookingId.HasValue && !existingFlights.Contains(entity.Id)) db.PassengerFlights.Add(new PassengerFlight { PassengerId = entity.Id, FlightBookingId = req.FlightBookingId.Value });
            }
            await db.SaveChangesAsync(ct); await Audit(db, user, "Passenger", null, Guid.Empty, "BulkAssign", null, new { count = entities.Count, req.RoomReservationId, req.FlightBookingId, req.NextAction }, ct);
            return Results.Ok(new { updated = entities.Count });
        }).RequireAuthorization("CanEdit");
    }

    private static void MapRooms(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/rooms");
        group.MapGet("/", async (string? operatorName, string? status, AppDbContext db, CancellationToken ct) =>
        {
            var query = db.RoomReservations.AsNoTracking().Include(x => x.Operator).Include(x => x.Passengers).AsQueryable();
            if (!string.IsNullOrWhiteSpace(operatorName)) query = query.Where(x => x.Operator.Name == operatorName);
            if (Enum.TryParse<VerificationStatus>(status, true, out var value)) query = query.Where(x => x.Status == value);
            return Results.Ok((await query.OrderBy(x => x.InternalCode).ToListAsync(ct)).Select(x => new { x.Id, x.InternalCode, Operator = new { x.Operator.Id, x.Operator.Name },
                x.Status, x.Hotel, x.RoomType, x.CheckIn, x.CheckOut, x.Nights, Occupants = x.Passengers.Select(p => new { p.Id, p.FullName }),
                x.ExpectedCapacity, x.CapacityOverride, x.CapacityOverrideReason, x.SourceReference, x.HotelReservationNumber, x.MealPlan, x.OperatorContact, x.Notes,
                Alerts = new[] { x.SpecificPropertyPending ? BusinessRules.TopTravelPropertyAlert : null, !x.CapacityOverride && x.Passengers.Count > x.ExpectedCapacity ? "Ocupación incompatible" : null }.Where(a => a != null), x.Version }));
        });
        group.MapPut("/{id:guid}", async (Guid id, RoomUpdateRequest req, IValidator<RoomUpdateRequest> validator, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct); if (!validation.IsValid) return Validation(validation);
            var room = await db.RoomReservations.Include(x => x.Passengers).SingleOrDefaultAsync(x => x.Id == id, ct); if (room is null) return Results.NotFound();
            if (room.Version != req.Version) return Conflict();
            if (req.Status == VerificationStatus.Confirmed && !req.CapacityOverride && room.Passengers.Count > req.ExpectedCapacity)
                return Results.BadRequest(new { message = "La ocupación supera la capacidad esperada.", occupants = room.Passengers.Count, capacity = req.ExpectedCapacity });
            var before = new { room.Status, room.Hotel, room.RoomType, room.CheckIn, room.CheckOut };
            room.InternalCode = req.InternalCode.Trim(); room.OperatorId = req.OperatorId; room.Status = req.Status; room.Hotel = Blank(req.Hotel); room.RoomType = Blank(req.RoomType);
            room.ExpectedCapacity = req.ExpectedCapacity; room.CapacityOverride = req.CapacityOverride; room.CapacityOverrideReason = Blank(req.CapacityOverrideReason);
            room.CheckIn = req.CheckIn; room.CheckOut = req.CheckOut; room.HotelReservationNumber = Blank(req.HotelReservationNumber); room.MealPlan = Blank(req.MealPlan);
            room.SourceReference = Blank(req.SourceReference); room.OperatorContact = Blank(req.OperatorContact); room.Notes = Blank(req.Notes);
            room.SpecificPropertyPending = await db.Operators.AnyAsync(x => x.Id == req.OperatorId && x.Name == "Top Travel", ct) && string.IsNullOrWhiteSpace(req.Hotel);
            if (req.Status == VerificationStatus.Confirmed) { room.VerifiedAt = DateTimeOffset.UtcNow; room.VerifiedById = UserId(user); }
            await db.SaveChangesAsync(ct); await Audit(db, user, "RoomReservation", null, id, "Update", before, new { room.Status, room.Hotel, room.RoomType, room.CheckIn, room.CheckOut }, ct);
            return Results.NoContent();
        }).RequireAuthorization("CanEdit");
    }

    private static void MapFlights(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/flights");
        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var entities = await db.FlightBookings.AsNoTracking().Include(x => x.Segments)
                .Include(x => x.PassengerFlights).ThenInclude(x => x.Passenger).ToListAsync(ct);
            return Results.Ok(entities.OrderBy(x => x.Pnr).Select(x => new { x.Id, x.Status, x.Airline, x.IssuingAgency, x.Pnr, x.GeneralReference, x.SourceReference, x.Notes,
                Segments = x.Segments.OrderBy(s => s.Sequence).ToList(), Passengers = x.PassengerFlights.Select(p => new { p.PassengerId, p.Passenger.FullName, p.ElectronicTicketNumber, p.TicketStatus }).ToList(), x.Version }));
        });
        group.MapPost("/", async (FlightBookingRequest req, IValidator<FlightBookingRequest> validator, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
            await SaveFlight(null, req, validator, db, user, ct)).RequireAuthorization("CanEdit");
        group.MapPut("/{id:guid}", async (Guid id, FlightBookingRequest req, IValidator<FlightBookingRequest> validator, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
            await SaveFlight(id, req, validator, db, user, ct)).RequireAuthorization("CanEdit");
        group.MapPut("/{flightId:guid}/passengers/{passengerId:guid}/ticket", async (Guid flightId, Guid passengerId, PassengerTicketRequest req, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var link = await db.PassengerFlights.SingleOrDefaultAsync(x => x.FlightBookingId == flightId && x.PassengerId == passengerId, ct);
            if (link is null) return Results.NotFound();
            var before = new { link.ElectronicTicketNumber, link.TicketStatus };
            link.ElectronicTicketNumber = Blank(req.ElectronicTicketNumber); link.TicketStatus = req.Status; link.Notes = Blank(req.Notes);
            await db.SaveChangesAsync(ct); await Audit(db, user, "PassengerFlight", passengerId, $"{passengerId}:{flightId}", "TicketUpdate", before, new { ticket = Mask(link.ElectronicTicketNumber), link.TicketStatus }, ct);
            return Results.NoContent();
        }).RequireAuthorization("CanEdit");
        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var entity = await db.FlightBookings.FindAsync([id], ct); if (entity is null) return Results.NotFound();
            db.FlightBookings.Remove(entity); await db.SaveChangesAsync(ct); await Audit(db, user, "FlightBooking", null, id, "Delete", null, null, ct); return Results.NoContent();
        }).RequireAuthorization("AdminOnly");
    }

    private static async Task<IResult> SaveFlight(Guid? id, FlightBookingRequest req, IValidator<FlightBookingRequest> validator,
        AppDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(req, ct); if (!validation.IsValid) return Validation(validation);
        FlightBooking entity;
        if (id.HasValue)
        {
            entity = await db.FlightBookings.Include(x => x.Segments).Include(x => x.PassengerFlights).SingleOrDefaultAsync(x => x.Id == id, ct) ?? null!;
            if (entity is null) return Results.NotFound();
            db.FlightSegments.RemoveRange(entity.Segments); db.PassengerFlights.RemoveRange(entity.PassengerFlights);
        }
        else
        {
            var trip = await db.Trips.SingleAsync(x => x.IsActive, ct); entity = new FlightBooking { TripId = trip.Id }; db.FlightBookings.Add(entity);
        }
        entity.Status = req.Status; entity.Airline = Blank(req.Airline); entity.IssuingAgency = Blank(req.IssuingAgency); entity.Pnr = Blank(req.Pnr);
        entity.GeneralReference = Blank(req.GeneralReference); entity.SourceReference = Blank(req.SourceReference); entity.Notes = Blank(req.Notes);
        if (req.Status == VerificationStatus.Confirmed) { entity.VerifiedAt = DateTimeOffset.UtcNow; entity.VerifiedById = UserId(user); }
        entity.Segments = req.Segments.Select(s => new FlightSegment { Type = s.Type, FlightNumber = Blank(s.FlightNumber), OriginAirport = Blank(s.OriginAirport),
            DestinationAirport = Blank(s.DestinationAirport), DepartureAt = s.DepartureAt, ArrivalAt = s.ArrivalAt, OriginTimeZone = Blank(s.OriginTimeZone),
            DestinationTimeZone = Blank(s.DestinationTimeZone), Sequence = s.Sequence }).ToList();
        entity.PassengerFlights = req.PassengerIds.Distinct().Select(passengerId => new PassengerFlight { PassengerId = passengerId }).ToList();
        await db.SaveChangesAsync(ct); await Audit(db, user, "FlightBooking", null, entity.Id, id.HasValue ? "Update" : "Create", null, new { entity.Pnr, entity.Status, passengers = req.PassengerIds.Count }, ct);
        return id.HasValue ? Results.NoContent() : Results.Created($"/api/flights/{entity.Id}", new { entity.Id });
    }

    private static void MapBaggage(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/baggage");
        group.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok(await db.BaggageEntitlements.AsNoTracking().Include(x => x.Passenger).Include(x => x.FlightBooking)
            .OrderBy(x => x.Passenger.FullName).Select(x => new { x.Id, x.PassengerId, x.Passenger.FullName, x.FlightBookingId, Pnr = x.FlightBooking == null ? null : x.FlightBooking.Pnr,
                x.Status, x.CheckedBagCount, x.WeightPerBagKg, x.AppliesOutbound, x.AppliesReturn, x.ExceptionReason, x.SourceReference, x.Notes, x.Version }).ToListAsync(ct)));
        group.MapPost("/", async (BaggageUpdateRequest req, IValidator<BaggageUpdateRequest> validator, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct); if (!validation.IsValid) return Validation(validation);
            var entity = await db.BaggageEntitlements.SingleOrDefaultAsync(x => x.PassengerId == req.PassengerId && x.FlightBookingId == req.FlightBookingId, ct);
            var created = entity is null; entity ??= new BaggageEntitlement { PassengerId = req.PassengerId, FlightBookingId = req.FlightBookingId };
            entity.Status = req.Status; entity.CheckedBagCount = req.CheckedBagCount; entity.WeightPerBagKg = req.WeightPerBagKg;
            entity.AppliesOutbound = req.AppliesOutbound; entity.AppliesReturn = req.AppliesReturn; entity.ExceptionReason = Blank(req.ExceptionReason);
            entity.SourceReference = Blank(req.SourceReference); entity.Notes = Blank(req.Notes);
            if (req.Status == VerificationStatus.Confirmed) { entity.VerifiedAt = DateTimeOffset.UtcNow; entity.VerifiedById = UserId(user); }
            if (created) db.BaggageEntitlements.Add(entity); await db.SaveChangesAsync(ct);
            await Audit(db, user, "BaggageEntitlement", req.PassengerId, entity.Id, created ? "Create" : "Update", null, new { entity.Status, entity.CheckedBagCount, entity.WeightPerBagKg }, ct);
            return Results.Ok(new { entity.Id });
        }).RequireAuthorization("CanEdit");
        group.MapPost("/confirm-group", async (GroupBaggageRequest req, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var ids = req.PassengerIds?.Distinct().ToList() ?? await db.PassengerFlights.Where(x => x.FlightBookingId == req.FlightBookingId).Select(x => x.PassengerId).ToListAsync(ct);
            foreach (var passengerId in ids)
            {
                var entity = await db.BaggageEntitlements.SingleOrDefaultAsync(x => x.PassengerId == passengerId && x.FlightBookingId == req.FlightBookingId, ct);
                if (entity is null) { entity = new BaggageEntitlement { PassengerId = passengerId, FlightBookingId = req.FlightBookingId }; db.BaggageEntitlements.Add(entity); }
                entity.Status = VerificationStatus.Confirmed; entity.CheckedBagCount = 1; entity.WeightPerBagKg = 23; entity.AppliesOutbound = true; entity.AppliesReturn = true;
                entity.SourceReference = Blank(req.SourceReference); entity.Notes = Blank(req.Notes); entity.VerifiedAt = DateTimeOffset.UtcNow; entity.VerifiedById = UserId(user);
            }
            await db.SaveChangesAsync(ct); await Audit(db, user, "BaggageEntitlement", null, req.FlightBookingId, "GroupConfirm", null, new { count = ids.Count }, ct);
            return Results.Ok(new { updated = ids.Count });
        }).RequireAuthorization("CanEdit");
    }

    private static void MapTransfer(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/transfer");
        group.MapGet("/", async (AppDbContext db, UserManager<AppUser> users, CancellationToken ct) =>
        {
            var entity = await db.TripTransferStatuses.AsNoTracking().SingleAsync(x => x.Trip.IsActive, ct);
            var updatedBy = entity.UpdatedByUserId.HasValue ? await users.FindByIdAsync(entity.UpdatedByUserId.Value.ToString()) : null;
            return Results.Ok(new TransferStatusResponse(entity.IsConfirmed, entity.ConfirmedAt, entity.Notes, updatedBy?.DisplayName, entity.UpdatedAt, entity.Version));
        });
        group.MapPut("/", async (TripTransferStatusRequest req, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var entity = await db.TripTransferStatuses.SingleAsync(x => x.Trip.IsActive, ct); if (entity.Version != req.Version) return Conflict();
            var before = new { entity.IsConfirmed, entity.Notes }; entity.IsConfirmed = req.IsConfirmed; entity.Notes = Blank(req.Notes);
            entity.ConfirmedAt = req.IsConfirmed ? DateTimeOffset.UtcNow : null; entity.UpdatedByUserId = UserId(user);
            await db.SaveChangesAsync(ct); await Audit(db, user, "TripTransferStatus", null, entity.Id, "Update", before, new { entity.IsConfirmed, entity.Notes }, ct);
            return Results.NoContent();
        }).RequireAuthorization("CanEdit");
    }

    private static void MapFollowUps(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/follow-ups");
        group.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok(await db.FollowUps.AsNoTracking().Include(x => x.Passenger)
            .OrderBy(x => x.Status).ThenBy(x => x.DueDate).Select(x => new { x.Id, x.TripId, x.PassengerId, Passenger = x.Passenger == null ? null : x.Passenger.FullName,
                x.RoomReservationId, x.Title, x.Description, x.DueDate, x.Status, x.Priority, x.ClosedAt, x.Version }).ToListAsync(ct)));
        group.MapPost("/", async (FollowUpRequest req, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title)) return Results.BadRequest(new { message = "El título es obligatorio." });
            var tripId = req.TripId ?? await db.Trips.Where(x => x.IsActive).Select(x => x.Id).SingleAsync(ct);
            var entity = new FollowUp { TripId = tripId, PassengerId = req.PassengerId, RoomReservationId = req.RoomReservationId,
                Title = req.Title.Trim(), Description = Blank(req.Description), DueDate = req.DueDate, Status = req.Status, Priority = req.Priority };
            if (req.Status == FollowUpStatus.Closed) { entity.ClosedAt = DateTimeOffset.UtcNow; entity.ClosedByUserId = UserId(user); }
            db.FollowUps.Add(entity); await db.SaveChangesAsync(ct); await Audit(db, user, "FollowUp", req.PassengerId, entity.Id, "Create", null, new { entity.Title, entity.DueDate }, ct);
            return Results.Created($"/api/follow-ups/{entity.Id}", new { entity.Id });
        }).RequireAuthorization("CanEdit");
    }

    private static void MapImportsExports(RouteGroupBuilder api)
    {
        var imports = api.MapGroup("/imports").RequireAuthorization("AdminOnly");
        imports.MapPost("/preview", async (HttpRequest request, ExcelImportService service, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct); var file = form.Files.GetFile("file"); if (file is null) return Results.BadRequest(new { message = "Adjuntá un archivo XLSX." });
            await using var stream = file.OpenReadStream(); return Results.Ok(await service.ProcessAsync(stream, file.FileName, true, UserId(user), ct));
        });
        imports.MapPost("/commit", async (HttpRequest request, ExcelImportService service, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct); var file = form.Files.GetFile("file"); if (file is null) return Results.BadRequest(new { message = "Adjuntá un archivo XLSX." });
            await using var stream = file.OpenReadStream(); var result = await service.ProcessAsync(stream, file.FileName, false, UserId(user), ct);
            return result.CanCommit ? Results.Ok(result) : Results.BadRequest(result);
        });
        imports.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok((await db.ImportRuns.AsNoTracking().ToListAsync(ct)).OrderByDescending(x => x.CreatedAt).Take(50)));
        var exports = api.MapGroup("/exports");
        exports.MapGet("/control.xlsx", async (ExcelExportService service, CancellationToken ct) => Results.File(await service.ExportAsync(ct), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"control-viaje-{DateTime.UtcNow:yyyyMMdd}.xlsx"));
        exports.MapGet("/passengers.csv", async (ExcelExportService service, CancellationToken ct) => Results.File(System.Text.Encoding.UTF8.GetPreamble().Concat(await service.ExportPassengersCsvAsync(ct)).ToArray(), "text/csv; charset=utf-8", $"pasajeros-{DateTime.UtcNow:yyyyMMdd}.csv"));
        exports.MapGet("/pending.xlsx", async (ExcelExportService service, CancellationToken ct) => Results.File(await service.ExportPendingAsync(ct), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"pendientes-{DateTime.UtcNow:yyyyMMdd}.xlsx"));
        exports.MapGet("/backup.json", async (ExcelExportService service, CancellationToken ct) => Results.Text(await service.ExportBackupJsonAsync(ct), "application/json")).RequireAuthorization("AdminOnly");
    }

    private static void MapAttachments(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/attachments");
        group.MapGet("/", async (Guid? passengerId, Guid? roomId, Guid? flightId, Guid? baggageId, AppDbContext db, CancellationToken ct) =>
        {
            var query = db.Attachments.AsNoTracking().AsQueryable();
            if (passengerId.HasValue) query = query.Where(x => x.PassengerId == passengerId); if (roomId.HasValue) query = query.Where(x => x.RoomReservationId == roomId);
            if (flightId.HasValue) query = query.Where(x => x.FlightBookingId == flightId); if (baggageId.HasValue) query = query.Where(x => x.BaggageEntitlementId == baggageId);
            var entries = await query.Select(x => new { x.Id, x.DocumentType, x.OriginalName, x.MimeType, x.Size, x.UploadedAt, x.Description }).ToListAsync(ct);
            return Results.Ok(entries.OrderByDescending(x => x.UploadedAt));
        });
        group.MapPost("/", async (HttpRequest request, AttachmentStorage storage, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct); var file = form.Files.GetFile("file"); if (file is null) return Results.BadRequest(new { message = "Falta el archivo." });
            if (!Enum.TryParse<DocumentType>(form["documentType"], true, out var type)) type = DocumentType.Other;
            Guid? Parse(string key) => Guid.TryParse(form[key], out var id) ? id : null;
            try { var stored = await storage.SaveAsync(file, type, UserId(user), form["description"], Parse("passengerId"), Parse("roomId"), Parse("flightId"), Parse("baggageId"), ct);
                return Results.Ok(new { stored.Entity.Id, stored.Entity.OriginalName, stored.Entity.MimeType, stored.Entity.Size, stored.Duplicate }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { message = ex.Message }); }
        }).RequireAuthorization("CanEdit");
        group.MapGet("/{id:guid}", async (Guid id, AttachmentStorage storage, CancellationToken ct) =>
        {
            try { var (entity, stream) = await storage.OpenAsync(id, ct); return Results.File(stream, entity.MimeType, entity.OriginalName, enableRangeProcessing: true); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });
        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var entity = await db.Attachments.FindAsync([id], ct); if (entity is null) return Results.NotFound();
            var path = entity.SecurePath; db.Attachments.Remove(entity); await db.SaveChangesAsync(ct); if (File.Exists(path)) File.Delete(path);
            await Audit(db, user, "Attachment", entity.PassengerId, id, "Delete", new { entity.OriginalName, entity.Sha256 }, null, ct); return Results.NoContent();
        }).RequireAuthorization("AdminOnly");
    }

    private static void MapUsers(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/users").RequireAuthorization("AdminOnly");
        group.MapGet("/", async (UserManager<AppUser> users) =>
        {
            var result = new List<object>(); foreach (var user in await users.Users.OrderBy(x => x.DisplayName).ToListAsync())
                result.Add(new { user.Id, user.Email, user.DisplayName, user.IsActive, roles = await users.GetRolesAsync(user), user.LockoutEnd });
            return Results.Ok(result);
        });
        group.MapPost("/", async (UserCreateRequest req, UserManager<AppUser> users) =>
        {
            var user = new AppUser { UserName = req.Email.Trim(), Email = req.Email.Trim(), DisplayName = req.DisplayName.Trim(), EmailConfirmed = true };
            var result = await users.CreateAsync(user, req.InitialPassword); if (!result.Succeeded) return IdentityErrors(result);
            await users.AddToRoleAsync(user, req.Role.ToString()); return Results.Created($"/api/users/{user.Id}", new { user.Id });
        });
        group.MapPut("/{id:guid}", async (Guid id, UserUpdateRequest req, UserManager<AppUser> users) =>
        {
            var user = await users.FindByIdAsync(id.ToString()); if (user is null) return Results.NotFound();
            user.DisplayName = req.DisplayName.Trim(); user.IsActive = req.IsActive; var update = await users.UpdateAsync(user); if (!update.Succeeded) return IdentityErrors(update);
            var current = await users.GetRolesAsync(user); await users.RemoveFromRolesAsync(user, current); await users.AddToRoleAsync(user, req.Role.ToString()); return Results.NoContent();
        });
        group.MapPost("/{id:guid}/reset-password", async (Guid id, AdminPasswordResetRequest req, UserManager<AppUser> users) =>
        {
            var user = await users.FindByIdAsync(id.ToString()); if (user is null) return Results.NotFound();
            var token = await users.GeneratePasswordResetTokenAsync(user); var result = await users.ResetPasswordAsync(user, token, req.NewPassword);
            return result.Succeeded ? Results.NoContent() : IdentityErrors(result);
        }).RequireRateLimiting("auth");
    }

    private static void MapReference(RouteGroupBuilder api)
    {
        api.MapGet("/trips", async (AppDbContext db, CancellationToken ct) => Results.Ok(await db.Trips.AsNoTracking().ToListAsync(ct)));
        api.MapGet("/operators", async (AppDbContext db, CancellationToken ct) => Results.Ok(await db.Operators.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct)));
    }

    private static void MapAudit(RouteGroupBuilder api) => api.MapGet("/audit", async (int page, int pageSize, string? entityType, string? entityId, Guid? passengerId, AppDbContext db, CancellationToken ct) =>
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize == 0 ? 50 : pageSize, 1, 100); var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(x => x.EntityName == entityType); if (!string.IsNullOrWhiteSpace(entityId)) query = query.Where(x => x.EntityId == entityId);
        if (passengerId.HasValue) query = query.Where(x => x.PassengerId == passengerId);
        var records = (await query.ToListAsync(ct)).OrderByDescending(x => x.At).ToList();
        return Results.Ok(new PagedResult<AuditLog>(records.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, records.Count));
    }).RequireAuthorization("AdminOnly");

    private static IResult Validation(FluentValidation.Results.ValidationResult result) => Results.ValidationProblem(result.Errors
        .GroupBy(x => char.ToLowerInvariant(x.PropertyName[0]) + x.PropertyName[1..]).ToDictionary(x => x.Key, x => x.Select(e => e.ErrorMessage).ToArray()));
    private static IResult IdentityErrors(IdentityResult result) => Results.ValidationProblem(result.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray()));
    private static IResult Conflict() => Results.Conflict(new { message = "El registro cambió; recargá la vista antes de guardar." });
    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? Mask(string? value) => string.IsNullOrWhiteSpace(value) ? null : $"***{value[^Math.Min(3, value.Length)..]}";
    private static async Task Audit(AppDbContext db, ClaimsPrincipal user, string entity, Guid? passengerId, object id, string action, object? before, object? after, CancellationToken ct)
    {
        db.AuditLogs.Add(new AuditLog { UserId = UserId(user), UserName = user.Identity?.Name, EntityName = entity, EntityId = id.ToString() ?? "", PassengerId = passengerId,
            Action = action, PreviousValue = before is null ? null : JsonSerializer.Serialize(before), NewValue = after is null ? null : JsonSerializer.Serialize(after) });
        await db.SaveChangesAsync(ct);
    }
}
