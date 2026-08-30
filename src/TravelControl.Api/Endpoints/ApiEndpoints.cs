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
        MapPublic(endpoints);
        MapAuth(endpoints);
        var api = endpoints.MapGroup("/api").RequireAuthorization();
        MapDashboard(api); MapPassengers(api); MapRooms(api); MapFlights(api); MapBaggage(api);
        MapTransfer(api); MapFollowUps(api); MapImportsExports(api); MapAttachments(api);
        MapUsers(api); MapReference(api); MapAudit(api);
        return endpoints;
    }

    private static void MapPublic(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/public").AllowAnonymous().RequireRateLimiting("public-read");
        group.MapGet("/dashboard", async (PublicReadService service, CancellationToken ct) =>
            service.Enabled ? Results.Ok(await service.GetDashboardAsync(ct)) : Results.NotFound());
        group.MapGet("/passengers", async (string? search, string? operatorName, string? overall, string? requirement,
            string? status, int? page, int? pageSize, PublicReadService service, CancellationToken ct) =>
            service.Enabled
                ? Results.Ok(await service.GetPassengersAsync(search, operatorName, overall, requirement, status, page ?? 1, pageSize ?? 25, ct))
                : Results.NotFound());
        group.MapGet("/passengers/{id:guid}", async (Guid id, PublicReadService service, CancellationToken ct) =>
        {
            if (!service.Enabled) return Results.NotFound();
            var passenger = await service.GetPassengerAsync(id, ct);
            return passenger is null ? Results.NotFound() : Results.Ok(passenger);
        });
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
        group.MapGet("/", async (string? search, string? passport, string? pnr, string? ticket, string? groupCode,
            string? operatorName, string? airline, string? overall, string? requirement, string? status, bool? propertyPending, bool? overdue,
            string? sortBy, string? sortDirection, int page, int pageSize, PassengerQueryService service, CancellationToken ct) =>
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
            if (!string.IsNullOrWhiteSpace(passport))
            {
                var normalizedPassport = TextNormalizer.Normalize(passport);
                query = query.Where(x => x.NormalizedPassportNumber != null && x.NormalizedPassportNumber.Contains(normalizedPassport));
            }
            if (!string.IsNullOrWhiteSpace(pnr)) query = query.Where(x => x.PassengerFlights.Any(f => f.FlightBooking.Pnr != null && f.FlightBooking.Pnr.Contains(pnr)));
            if (!string.IsNullOrWhiteSpace(ticket)) query = query.Where(x => x.PassengerFlights.Any(f => f.ElectronicTicketNumber != null && f.ElectronicTicketNumber.Contains(ticket)));
            if (!string.IsNullOrWhiteSpace(groupCode)) query = query.Where(x => x.RoomReservation != null && x.RoomReservation.InternalCode.Contains(groupCode));
            if (!string.IsNullOrWhiteSpace(operatorName)) query = query.Where(x => x.PrimaryOperator != null && x.PrimaryOperator.Name == operatorName);
            if (string.Equals(airline, "none", StringComparison.OrdinalIgnoreCase))
                query = query.Where(x => x.PassengerFlights.All(f => f.FlightBooking.Airline == null || f.FlightBooking.Airline == ""));
            else if (!string.IsNullOrWhiteSpace(airline))
                query = query.Where(x => x.PassengerFlights.Any(f => f.FlightBooking.Airline != null && f.FlightBooking.Airline.Contains(airline)));
            if (propertyPending == true) query = query.Where(x => x.RoomReservation != null && x.RoomReservation.SpecificPropertyPending);
            if (overdue == true)
            {
                var currentDate = DateOnly.FromDateTime(DateTime.UtcNow);
                query = query.Where(x => x.NextActionDueDate.HasValue && x.NextActionDueDate < currentDate);
            }
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var entities = await query.ToListAsync(ct);
            var passengerIds = entities.Select(x => x.Id).ToArray();
            var evidence = await service.EvidenceAsync(passengerIds, ct);
            IEnumerable<PassengerListItem> mapped = entities.Select(x => PassengerQueryService.Map(x, today,
                evidence.GetValueOrDefault(x.Id) ?? new PassengerEvidenceState()));
            if (Enum.TryParse<PassengerOverallStatus>(overall, true, out var overallValue)) mapped = mapped.Where(x => x.OverallStatus == overallValue);
            if (!string.IsNullOrWhiteSpace(requirement)) mapped = mapped.Where(x =>
            {
                var item = x.Requirements.FirstOrDefault(r => r.Key == requirement);
                return item is not null && (!Enum.TryParse<VerificationStatus>(status, true, out var statusValue)
                    ? !BusinessRules.IsResolved(item) : item.Status == statusValue);
            });
            var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
            mapped = (sortBy ?? "name").ToLowerInvariant() switch
            {
                "operator" => descending ? mapped.OrderByDescending(x => x.Operator).ThenBy(x => x.FullName) : mapped.OrderBy(x => x.Operator).ThenBy(x => x.FullName),
                "group" => descending ? mapped.OrderByDescending(x => x.RoomCode).ThenBy(x => x.FullName) : mapped.OrderBy(x => x.RoomCode).ThenBy(x => x.FullName),
                "hotel" => descending ? mapped.OrderByDescending(x => x.Hotel).ThenBy(x => x.FullName) : mapped.OrderBy(x => x.Hotel).ThenBy(x => x.FullName),
                "overall" => descending ? mapped.OrderByDescending(x => x.OverallStatus).ThenBy(x => x.FullName) : mapped.OrderBy(x => x.OverallStatus).ThenBy(x => x.FullName),
                "progress" => descending ? mapped.OrderByDescending(x => x.ProgressPercent).ThenBy(x => x.FullName) : mapped.OrderBy(x => x.ProgressPercent).ThenBy(x => x.FullName),
                "due" => descending ? mapped.OrderByDescending(x => x.NextActionDueDate).ThenBy(x => x.FullName) : mapped.OrderBy(x => x.NextActionDueDate).ThenBy(x => x.FullName),
                "updated" => descending ? mapped.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.FullName) : mapped.OrderBy(x => x.UpdatedAt).ThenBy(x => x.FullName),
                _ => descending ? mapped.OrderByDescending(x => x.FullName) : mapped.OrderBy(x => x.FullName)
            };
            var result = mapped.ToList();
            return Results.Ok(new PagedResult<PassengerListItem>(result.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, result.Count));
        });
        group.MapGet("/{id:guid}", async (Guid id, PassengerQueryService service, EvidenceResolver evidenceResolver, CancellationToken ct) =>
        {
            var p = await service.BaseQuery().SingleOrDefaultAsync(x => x.Id == id, ct); if (p is null) return Results.NotFound();
            var evidence = (await service.EvidenceAsync([id], ct)).GetValueOrDefault(id) ?? new PassengerEvidenceState();
            var relatedEvidence = await evidenceResolver.GetPassengerEvidenceAsync(id, ct);
            return Results.Ok(new
            {
                passenger = new { p.Id, p.FullName, p.BirthDate, p.Nationality, p.PassportExpiry, p.Phone, p.Email, p.EstimatedHotelArrival,
                    p.DietaryRestrictions, p.Notes, p.NextAction, p.NextActionDueDate, p.PassportReviewStatus, p.DocumentationStatus,
                    p.DocumentationExceptionReason, p.Version,
                    PrimaryOperator = p.PrimaryOperator is null ? null : new { p.PrimaryOperator.Id, p.PrimaryOperator.Name },
                    RoomReservation = p.RoomReservation is null ? null : new { p.RoomReservation.Id, p.RoomReservation.InternalCode, p.RoomReservation.Hotel,
                        p.RoomReservation.RoomType, p.RoomReservation.CheckIn, p.RoomReservation.CheckOut, p.RoomReservation.Status },
                    PassengerFlights = p.PassengerFlights.Select(x => new { x.FlightBookingId, x.ElectronicTicketNumber, x.TicketStatus, x.Notes,
                        x.Version, x.UpdatedAt, x.UpdatedById,
                        Booking = new { x.FlightBooking.Pnr, x.FlightBooking.Airline, x.FlightBooking.Status, x.FlightBooking.SourceReference,
                            Segments = x.FlightBooking.Segments.OrderBy(s => s.Sequence).Select(s => new { s.Id, s.Type, s.FlightNumber, s.OriginAirport, s.DestinationAirport, s.DepartureAt, s.ArrivalAt }) } }),
                    BaggageEntitlements = p.BaggageEntitlements.Select(x => new { x.Id, x.FlightBookingId, x.Status, x.CheckedBagCount,
                        x.WeightPerBagKg, x.Includes23Kg, x.AppliesOutbound, x.AppliesReturn, x.ExceptionReason, x.SourceReference, x.Notes, x.Version }),
                    FollowUps = p.FollowUps.Select(x => new { x.Id, x.Title, x.Description, x.DueDate, x.Status, x.Priority, x.Version }) },
                computed = BusinessRules.CalculatePassenger(p, DateOnly.FromDateTime(DateTime.UtcNow), evidence),
                maskedPassport = PassengerQueryService.MaskPassport(p.PassportNumber),
                relatedEvidence
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
        group.MapPut("/{id:guid}", async (Guid id, UpdatePassengerRequest req, IValidator<UpdatePassengerRequest> validator,
            AppDbContext db, EvidenceResolver evidenceResolver, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct); if (!validation.IsValid) return Validation(validation);
            var entity = await db.Passengers.FindAsync([id], ct); if (entity is null) return Results.NotFound();
            if (entity.Version != req.Version) return Conflict();
            if (req.DocumentationStatus == VerificationStatus.Confirmed)
            {
                var missing = new List<string>();
                if (req.PassportReviewStatus != VerificationStatus.Confirmed) missing.Add("pasaporte revisado");
                var links = await db.PassengerFlights.Include(x => x.FlightBooking).ThenInclude(x => x.Segments)
                    .Where(x => x.PassengerId == id).ToListAsync(ct);
                if (!links.Any(x => x.TicketStatus == VerificationStatus.Confirmed
                    && BusinessRules.FlightCanBeConfirmed(x.FlightBooking, x, out _))) missing.Add("ticket efectivo");
                var room = req.RoomReservationId.HasValue
                    ? await db.RoomReservations.Include(x => x.Passengers).SingleOrDefaultAsync(x => x.Id == req.RoomReservationId, ct)
                    : null;
                var roomEvidence = room is null ? [] : await evidenceResolver.GetRoomEvidenceAsync([room.Id], ct);
                if (room?.Status != VerificationStatus.Confirmed || !BusinessRules.RoomCanBeConfirmed(room, roomEvidence.Contains(room.Id), out _)) missing.Add("habitación efectiva");
                var passengerEvidence = (await evidenceResolver.GetForPassengersAsync([id], ct)).GetValueOrDefault(id) ?? new PassengerEvidenceState();
                if (!passengerEvidence.HasAirTicketEvidence) missing.Add("comprobante o referencia de vuelo");
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
                if (req.FlightBookingId.HasValue && !existingFlights.Contains(entity.Id)) db.PassengerFlights.Add(new PassengerFlight
                    { PassengerId = entity.Id, FlightBookingId = req.FlightBookingId.Value, Version = 1, UpdatedById = UserId(user) });
            }
            if (req.FlightBookingId.HasValue)
            {
                var booking = await db.FlightBookings.Include(x => x.Segments).Include(x => x.PassengerFlights)
                    .SingleAsync(x => x.Id == req.FlightBookingId.Value, ct);
                booking.Status = BusinessRules.DeriveFlightBookingStatus(booking);
            }
            await db.SaveChangesAsync(ct); await Audit(db, user, "Passenger", null, Guid.Empty, "BulkAssign", null, new { count = entities.Count, req.RoomReservationId, req.FlightBookingId, req.NextAction }, ct);
            return Results.Ok(new { updated = entities.Count });
        }).RequireAuthorization("CanEdit");
    }

    private static void MapRooms(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/rooms");
        group.MapGet("/", async (string? operatorName, string? status, AppDbContext db, EvidenceResolver evidenceResolver, CancellationToken ct) =>
        {
            var query = db.RoomReservations.AsNoTracking().Include(x => x.Operator).Include(x => x.Passengers).AsQueryable();
            if (!string.IsNullOrWhiteSpace(operatorName)) query = query.Where(x => x.Operator.Name == operatorName);
            if (Enum.TryParse<VerificationStatus>(status, true, out var value)) query = query.Where(x => x.Status == value);
            var rooms = await query.OrderBy(x => x.InternalCode).ToListAsync(ct);
            var withVoucher = await evidenceResolver.GetRoomEvidenceAsync(rooms.Select(x => x.Id), ct);
            return Results.Ok(rooms.Select(x => new { x.Id, x.InternalCode, Operator = new { x.Operator.Id, x.Operator.Name },
                StoredStatus = x.Status,
                Status = x.Status == VerificationStatus.Confirmed
                    && !BusinessRules.RoomCanBeConfirmed(x, withVoucher.Contains(x.Id), out _) ? VerificationStatus.ToVerify : x.Status,
                EffectiveStatus = x.Status == VerificationStatus.Confirmed
                    && !BusinessRules.RoomCanBeConfirmed(x, withVoucher.Contains(x.Id), out _) ? VerificationStatus.ToVerify : x.Status,
                HasHotelVoucherEvidence = withVoucher.Contains(x.Id), x.Hotel, x.RoomType, x.CheckIn, x.CheckOut, x.Nights, Occupants = x.Passengers.Select(p => new { p.Id, p.FullName }),
                x.ExpectedCapacity, x.CapacityOverride, x.CapacityOverrideReason, x.SourceReference, x.HotelReservationNumber, x.MealPlan, x.OperatorContact, x.Notes,
                Alerts = new[] { x.SpecificPropertyPending ? BusinessRules.TopTravelPropertyAlert : null, !x.CapacityOverride && x.Passengers.Count > x.ExpectedCapacity ? "Ocupación incompatible" : null }.Where(a => a != null), x.Version }));
        });
        group.MapPut("/{id:guid}", async (Guid id, RoomUpdateRequest req, IValidator<RoomUpdateRequest> validator,
            AppDbContext db, EvidenceResolver evidenceResolver, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct); if (!validation.IsValid) return Validation(validation);
            var room = await db.RoomReservations.Include(x => x.Passengers).SingleOrDefaultAsync(x => x.Id == id, ct); if (room is null) return Results.NotFound();
            if (room.Version != req.Version) return Conflict();
            var before = new { room.Status, room.Hotel, room.RoomType, room.CheckIn, room.CheckOut };
            room.InternalCode = req.InternalCode.Trim(); room.OperatorId = req.OperatorId; room.Status = req.Status; room.Hotel = Blank(req.Hotel); room.RoomType = Blank(req.RoomType);
            room.ExpectedCapacity = req.ExpectedCapacity; room.CapacityOverride = req.CapacityOverride; room.CapacityOverrideReason = Blank(req.CapacityOverrideReason);
            room.CheckIn = req.CheckIn; room.CheckOut = req.CheckOut; room.HotelReservationNumber = Blank(req.HotelReservationNumber); room.MealPlan = Blank(req.MealPlan);
            room.SourceReference = Blank(req.SourceReference); room.OperatorContact = Blank(req.OperatorContact); room.Notes = Blank(req.Notes);
            var operatorName = await db.Operators.Where(x => x.Id == req.OperatorId).Select(x => x.Name).SingleOrDefaultAsync(ct);
            if (operatorName is null) return Results.BadRequest(new { message = "La operadora seleccionada no existe." });
            room.SpecificPropertyPending = BusinessRules.IsSpecificPropertyPending(operatorName, req.Hotel);
            var withVoucher = await evidenceResolver.GetRoomEvidenceAsync([room.Id], ct);
            if (req.Status == VerificationStatus.Confirmed && !BusinessRules.RoomCanBeConfirmed(room, withVoucher.Contains(room.Id), out var missing))
                return Results.BadRequest(new { message = "No se puede confirmar la habitación.", missing });
            if (req.Status == VerificationStatus.Confirmed) { room.VerifiedAt = DateTimeOffset.UtcNow; room.VerifiedById = UserId(user); }
            try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Conflict(); }
            await Audit(db, user, "RoomReservation", null, id, "Update", before, new { room.Status, room.Hotel, room.RoomType, room.CheckIn, room.CheckOut }, ct);
            return Results.NoContent();
        }).RequireAuthorization("CanEdit");
        group.MapPut("/{id:guid}/occupants", async (Guid id, RoomOccupantsRequest req, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var room = await db.RoomReservations.Include(x => x.Passengers).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (room is null) return Results.NotFound();
            if (room.Version != req.Version) return Conflict();
            var requested = req.PassengerIds.Distinct().ToArray();
            if (requested.Length > room.ExpectedCapacity && (!room.CapacityOverride || string.IsNullOrWhiteSpace(room.CapacityOverrideReason)))
                return Results.BadRequest(new { message = "La ocupación supera la capacidad esperada. Activá la excepción y justificála antes de continuar.", occupants = requested.Length, capacity = room.ExpectedCapacity });
            var target = await db.Passengers.Where(x => requested.Contains(x.Id)).ToListAsync(ct);
            if (target.Count != requested.Length) return Results.BadRequest(new { message = "Uno o más pasajeros no existen." });
            var previous = room.Passengers.Select(x => x.Id).ToArray();
            foreach (var passenger in room.Passengers.Where(x => !requested.Contains(x.Id)).ToArray()) passenger.RoomReservationId = null;
            foreach (var passenger in target) passenger.RoomReservationId = room.Id;
            room.UpdatedAt = DateTimeOffset.UtcNow;
            try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Conflict(); }
            await Audit(db, user, "RoomReservation", null, id, "OccupantsUpdate", new { passengerIds = previous }, new { passengerIds = requested }, ct);
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
                Segments = x.Segments.OrderBy(s => s.Sequence).Select(s => new { s.Id, s.Type, s.FlightNumber, s.OriginAirport,
                    s.DestinationAirport, s.DepartureAt, s.ArrivalAt, s.OriginTimeZone, s.DestinationTimeZone, s.Sequence }).ToList(),
                Passengers = x.PassengerFlights.Select(p => new { p.PassengerId, p.Passenger.FullName, p.ElectronicTicketNumber, p.TicketStatus, p.Notes, p.Version, p.UpdatedAt, p.UpdatedById }).ToList(), x.Version }));
        });
        group.MapPost("/", async (FlightBookingRequest req, IValidator<FlightBookingRequest> validator, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
            await SaveFlight(null, req, validator, db, user, ct)).RequireAuthorization("CanEdit");
        group.MapPut("/{id:guid}", async (Guid id, FlightBookingRequest req, IValidator<FlightBookingRequest> validator, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
            await SaveFlight(id, req, validator, db, user, ct)).RequireAuthorization("CanEdit");
        group.MapPut("/{flightId:guid}/passengers/{passengerId:guid}/ticket", async (Guid flightId, Guid passengerId, PassengerTicketRequest req, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var link = await db.PassengerFlights.Include(x => x.FlightBooking).ThenInclude(x => x.Segments)
                .Include(x => x.FlightBooking).ThenInclude(x => x.PassengerFlights)
                .SingleOrDefaultAsync(x => x.FlightBookingId == flightId && x.PassengerId == passengerId, ct);
            if (link is null) return Results.NotFound();
            if (link.Version != req.Version)
                return Results.Conflict(new { message = "El ticket cambió desde que abriste la ficha. Recargá antes de guardar." });
            var before = new { ticket = Mask(link.ElectronicTicketNumber), link.TicketStatus };
            link.ElectronicTicketNumber = Blank(req.ElectronicTicketNumber); link.TicketStatus = req.Status; link.Notes = Blank(req.Notes);
            link.UpdatedById = UserId(user);
            if (req.Status == VerificationStatus.NotApplicable && string.IsNullOrWhiteSpace(req.Notes))
                return Results.BadRequest(new { message = "No aplica requiere una justificación." });
            if (req.Status == VerificationStatus.Confirmed && !BusinessRules.FlightCanBeConfirmed(link.FlightBooking, link, out var missing))
                return Results.BadRequest(new { message = "No se puede confirmar el ticket.", missing });
            link.FlightBooking.Status = BusinessRules.DeriveFlightBookingStatus(link.FlightBooking);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { message = "El ticket cambió desde que abriste la ficha. Recargá antes de guardar." });
            }
            await Audit(db, user, "PassengerFlight", passengerId, $"{passengerId}:{flightId}", "TicketUpdate", before,
                new { ticket = Mask(link.ElectronicTicketNumber), link.TicketStatus, link.UpdatedById, link.UpdatedAt }, ct);
            return Results.Ok(new { link.Version, link.UpdatedAt, link.FlightBooking.Status });
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
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        FlightBooking entity;
        object? before = null;
        if (id.HasValue)
        {
            entity = await db.FlightBookings.Include(x => x.Segments).Include(x => x.PassengerFlights).SingleOrDefaultAsync(x => x.Id == id, ct) ?? null!;
            if (entity is null) return Results.NotFound();
            if (entity.Version != req.Version) return Conflict();
            before = new { entity.Status, entity.Airline, entity.Pnr, segmentIds = entity.Segments.Select(x => x.Id), passengerIds = entity.PassengerFlights.Select(x => x.PassengerId) };
        }
        else
        {
            var trip = await db.Trips.SingleAsync(x => x.IsActive, ct); entity = new FlightBooking { TripId = trip.Id }; db.FlightBookings.Add(entity);
        }
        entity.Airline = Blank(req.Airline); entity.IssuingAgency = Blank(req.IssuingAgency); entity.Pnr = Blank(req.Pnr);
        entity.GeneralReference = Blank(req.GeneralReference); entity.SourceReference = Blank(req.SourceReference); entity.Notes = Blank(req.Notes);
        var requestedSegmentIds = req.Segments.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToHashSet();
        if (requestedSegmentIds.Count != req.Segments.Count(x => x.Id.HasValue)
            || requestedSegmentIds.Any(segmentId => entity.Segments.All(x => x.Id != segmentId)))
            return Results.BadRequest(new { message = "Uno o más segmentos no pertenecen a esta reserva." });
        foreach (var segment in entity.Segments.Where(x => !requestedSegmentIds.Contains(x.Id)).ToArray())
        {
            entity.Segments.Remove(segment);
            db.FlightSegments.Remove(segment);
        }
        foreach (var item in req.Segments)
        {
            var segment = item.Id.HasValue ? entity.Segments.Single(x => x.Id == item.Id.Value) : new FlightSegment { FlightBooking = entity };
            segment.Type = item.Type; segment.FlightNumber = Blank(item.FlightNumber); segment.OriginAirport = Blank(item.OriginAirport);
            segment.DestinationAirport = Blank(item.DestinationAirport); segment.DepartureAt = item.DepartureAt; segment.ArrivalAt = item.ArrivalAt;
            segment.OriginTimeZone = Blank(item.OriginTimeZone); segment.DestinationTimeZone = Blank(item.DestinationTimeZone); segment.Sequence = item.Sequence;
            if (!item.Id.HasValue) entity.Segments.Add(segment);
        }

        var requestedPassengers = req.PassengerIds.Distinct().ToHashSet();
        if (await db.Passengers.CountAsync(x => requestedPassengers.Contains(x.Id), ct) != requestedPassengers.Count)
            return Results.BadRequest(new { message = "Uno o más pasajeros no existen." });
        var removedLinks = entity.PassengerFlights.Where(x => !requestedPassengers.Contains(x.PassengerId)).ToArray();
        var confirmedRemovalIds = (req.ConfirmedPassengerRemovalIds ?? []).ToHashSet();
        var protectedRemovals = removedLinks.Where(x => x.TicketStatus == VerificationStatus.Confirmed && !confirmedRemovalIds.Contains(x.PassengerId))
            .Select(x => x.PassengerId).ToArray();
        if (protectedRemovals.Length > 0)
            return Results.Conflict(new { message = "Retirar un pasajero con ticket confirmado requiere confirmación explícita.", passengerIds = protectedRemovals });
        foreach (var link in removedLinks)
        {
            entity.PassengerFlights.Remove(link);
            db.PassengerFlights.Remove(link);
        }
        var existingPassengers = entity.PassengerFlights.Select(x => x.PassengerId).ToHashSet();
        foreach (var passengerId in requestedPassengers.Where(x => !existingPassengers.Contains(x)))
            entity.PassengerFlights.Add(new PassengerFlight { PassengerId = passengerId, FlightBooking = entity, Version = 1, UpdatedById = UserId(user) });

        entity.Status = BusinessRules.DeriveFlightBookingStatus(entity);
        if (entity.Status == VerificationStatus.Confirmed)
        {
            entity.VerifiedAt = DateTimeOffset.UtcNow;
            entity.VerifiedById = UserId(user);
        }
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Conflict(); }
        await Audit(db, user, "FlightBooking", null, entity.Id, id.HasValue ? "Update" : "Create", before,
            new { entity.Pnr, entity.Status, segmentIds = entity.Segments.Select(x => x.Id), passengerIds = requestedPassengers }, ct);
        await transaction.CommitAsync(ct);
        return id.HasValue ? Results.NoContent() : Results.Created($"/api/flights/{entity.Id}", new { entity.Id });
    }

    private static void MapBaggage(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/baggage");
        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var entries = await db.BaggageEntitlements.AsNoTracking().Include(x => x.Passenger).Include(x => x.FlightBooking)
                .OrderBy(x => x.Passenger.FullName).ToListAsync(ct);
            var baggageIds = entries.Select(x => x.Id).ToArray();
            var flightIds = entries.Where(x => x.FlightBookingId.HasValue).Select(x => x.FlightBookingId!.Value).Distinct().ToArray();
            var proofs = await db.AttachmentLinks.AsNoTracking().Where(x => x.EvidenceType == DocumentType.BaggageProof
                    && (x.BaggageEntitlementId.HasValue && baggageIds.Contains(x.BaggageEntitlementId.Value)
                        || x.FlightBookingId.HasValue && flightIds.Contains(x.FlightBookingId.Value)))
                .Select(x => new { x.BaggageEntitlementId, x.FlightBookingId }).ToListAsync(ct);
            return Results.Ok(entries.Select(x => new { x.Id, x.PassengerId, x.Passenger.FullName, x.FlightBookingId,
                Pnr = x.FlightBooking?.Pnr, x.Status, x.CheckedBagCount, x.WeightPerBagKg, x.AppliesOutbound, x.AppliesReturn,
                x.ExceptionReason, x.SourceReference, x.Notes, x.Version,
                EvidenceLabel = proofs.Any(p => p.BaggageEntitlementId == x.Id) ? "Comprobante directo"
                    : proofs.Any(p => p.FlightBookingId == x.FlightBookingId) ? "Comprobante compartido por PNR" : "Sin evidencia" }));
        });
        group.MapPost("/", async (BaggageUpdateRequest req, IValidator<BaggageUpdateRequest> validator, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
            await SaveBaggage(null, req, validator, db, user, ct)).RequireAuthorization("CanEdit");
        group.MapPut("/{id:guid}", async (Guid id, BaggageUpdateRequest req, IValidator<BaggageUpdateRequest> validator,
            AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
            await SaveBaggage(id, req, validator, db, user, ct)).RequireAuthorization("CanEdit");
        group.MapPost("/confirm-group", async (GroupBaggageRequest req, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var booking = await db.FlightBookings.Include(x => x.Segments).Include(x => x.PassengerFlights)
                .SingleOrDefaultAsync(x => x.Id == req.FlightBookingId, ct);
            if (booking is null) return Results.NotFound();
            var associated = booking.PassengerFlights.ToDictionary(x => x.PassengerId);
            var ids = req.PassengerIds?.Distinct().ToList() ?? associated.Keys.ToList();
            var existingByPassenger = await db.BaggageEntitlements
                .Where(x => x.FlightBookingId == req.FlightBookingId && ids.Contains(x.PassengerId))
                .ToDictionaryAsync(x => x.PassengerId, ct);
            var skipped = new List<object>();
            var updated = 0;
            foreach (var passengerId in ids)
            {
                if (!associated.TryGetValue(passengerId, out var link))
                {
                    skipped.Add(new { passengerId, reason = "Pasajero no asociado al PNR" });
                    continue;
                }
                string[] ticketMissing = [];
                if (link.TicketStatus != VerificationStatus.Confirmed || !BusinessRules.FlightCanBeConfirmed(booking, link, out ticketMissing))
                {
                    skipped.Add(new { passengerId, reason = ticketMissing.FirstOrDefault() is { } reason ? $"Ticket todavía no confirmado: {reason}" : "Ticket todavía no confirmado" });
                    continue;
                }
                if (!existingByPassenger.TryGetValue(passengerId, out var entity))
                {
                    entity = new BaggageEntitlement { PassengerId = passengerId, FlightBookingId = req.FlightBookingId };
                    db.BaggageEntitlements.Add(entity);
                    existingByPassenger[passengerId] = entity;
                }
                entity.Status = VerificationStatus.Confirmed; entity.CheckedBagCount = 1; entity.WeightPerBagKg = 23; entity.AppliesOutbound = true; entity.AppliesReturn = true;
                entity.SourceReference = Blank(req.SourceReference); entity.Notes = Blank(req.Notes); entity.VerifiedAt = DateTimeOffset.UtcNow; entity.VerifiedById = UserId(user);
                updated++;
            }
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return BaggageConflict(); }
            catch (DbUpdateException) { return BaggageConflict(); }
            await Audit(db, user, "BaggageEntitlement", null, req.FlightBookingId, "GroupConfirm", null, new { updated, skipped = skipped.Count }, ct);
            return Results.Ok(new { updated, skipped });
        }).RequireAuthorization("CanEdit");
    }

    private static async Task<IResult> SaveBaggage(
        Guid? id,
        BaggageUpdateRequest req,
        IValidator<BaggageUpdateRequest> validator,
        AppDbContext db,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(req, ct);
        if (!validation.IsValid) return Validation(validation);
        if (!req.FlightBookingId.HasValue)
            return Results.BadRequest(new { message = "Debe asociarse una reserva aérea." });

        BaggageEntitlement entity;
        object? before = null;
        if (id.HasValue)
        {
            entity = await db.BaggageEntitlements.SingleOrDefaultAsync(x => x.Id == id.Value, ct)
                ?? null!;
            if (entity is null) return Results.NotFound();
            if (entity.PassengerId != req.PassengerId || entity.FlightBookingId != req.FlightBookingId)
                return Results.BadRequest(new { message = "No se puede cambiar el pasajero ni la reserva de una franquicia existente." });
            if (entity.Version != req.Version) return BaggageConflict();
            before = new { entity.Status, entity.CheckedBagCount, entity.WeightPerBagKg, entity.Version };
        }
        else
        {
            if (req.Version != 0) return Results.BadRequest(new { message = "Una franquicia nueva debe enviarse con versión 0." });
            if (await db.BaggageEntitlements.AnyAsync(x => x.PassengerId == req.PassengerId && x.FlightBookingId == req.FlightBookingId, ct))
                return Results.Conflict(new { message = "Ya existe una franquicia para este pasajero y reserva." });
            entity = new BaggageEntitlement { PassengerId = req.PassengerId, FlightBookingId = req.FlightBookingId };
            db.BaggageEntitlements.Add(entity);
        }

        entity.Status = req.Status;
        entity.CheckedBagCount = req.CheckedBagCount;
        entity.WeightPerBagKg = req.WeightPerBagKg;
        entity.AppliesOutbound = req.AppliesOutbound;
        entity.AppliesReturn = req.AppliesReturn;
        entity.ExceptionReason = Blank(req.ExceptionReason);
        entity.SourceReference = Blank(req.SourceReference);
        entity.Notes = Blank(req.Notes);
        if (req.Status == VerificationStatus.Confirmed)
        {
            var link = await db.PassengerFlights.Include(x => x.FlightBooking).ThenInclude(x => x.Segments)
                .SingleOrDefaultAsync(x => x.PassengerId == req.PassengerId && x.FlightBookingId == req.FlightBookingId, ct);
            var hasEffectiveTicket = link?.TicketStatus == VerificationStatus.Confirmed
                && BusinessRules.FlightCanBeConfirmed(link.FlightBooking, link, out _);
            if (!BusinessRules.BaggageCanBeConfirmed(entity, hasEffectiveTicket, out var missing))
                return Results.BadRequest(new { message = "No se puede confirmar la maleta.", missing });
            entity.VerifiedAt = DateTimeOffset.UtcNow;
            entity.VerifiedById = UserId(user);
        }

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return BaggageConflict(); }
        catch (DbUpdateException) { return Results.Conflict(new { message = "Ya existe una franquicia para este pasajero y reserva." }); }
        await Audit(db, user, "BaggageEntitlement", req.PassengerId, entity.Id, id.HasValue ? "Update" : "Create", before,
            new { entity.Status, entity.CheckedBagCount, entity.WeightPerBagKg, entity.Version }, ct);
        return id.HasValue ? Results.NoContent() : Results.Created($"/api/baggage/{entity.Id}", new { entity.Id, entity.Version });
    }

    private static IResult BaggageConflict() => Results.Conflict(new
    {
        message = "El registro de equipaje cambió; recargá la vista antes de guardar."
    });

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
        group.MapPut("/{id:guid}", async (Guid id, FollowUpRequest req, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title)) return Results.BadRequest(new { message = "El título es obligatorio." });
            var entity = await db.FollowUps.FindAsync([id], ct); if (entity is null) return Results.NotFound();
            if (entity.Version != req.Version) return Conflict();
            var before = new { entity.Title, entity.Status, entity.Priority, entity.DueDate };
            entity.PassengerId = req.PassengerId; entity.RoomReservationId = req.RoomReservationId; entity.Title = req.Title.Trim();
            entity.Description = Blank(req.Description); entity.DueDate = req.DueDate; entity.Priority = req.Priority; entity.Status = req.Status;
            if (req.Status == FollowUpStatus.Closed) { entity.ClosedAt ??= DateTimeOffset.UtcNow; entity.ClosedByUserId = UserId(user); }
            else { entity.ClosedAt = null; entity.ClosedByUserId = null; }
            try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Conflict(); }
            await Audit(db, user, "FollowUp", entity.PassengerId, id, "Update", before, new { entity.Title, entity.Status, entity.Priority, entity.DueDate }, ct);
            return Results.NoContent();
        }).RequireAuthorization("CanEdit");
        group.MapPost("/{id:guid}/close", async (Guid id, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
            await SetFollowUpStatus(id, FollowUpStatus.Closed, db, user, ct)).RequireAuthorization("CanEdit");
        group.MapPost("/{id:guid}/reopen", async (Guid id, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
            await SetFollowUpStatus(id, FollowUpStatus.Open, db, user, ct)).RequireAuthorization("CanEdit");
    }

    private static async Task<IResult> SetFollowUpStatus(Guid id, FollowUpStatus status, AppDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var entity = await db.FollowUps.FindAsync([id], ct); if (entity is null) return Results.NotFound();
        var before = entity.Status; entity.Status = status;
        entity.ClosedAt = status == FollowUpStatus.Closed ? DateTimeOffset.UtcNow : null;
        entity.ClosedByUserId = status == FollowUpStatus.Closed ? UserId(user) : null;
        await db.SaveChangesAsync(ct); await Audit(db, user, "FollowUp", entity.PassengerId, id,
            status == FollowUpStatus.Closed ? "Close" : "Reopen", new { status = before }, new { entity.Status }, ct);
        return Results.NoContent();
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
        imports.MapPost("/identification/preview", async (HttpRequest request, IdentificationImportService service, CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null) return Results.BadRequest(new { message = "Adjuntá un archivo XLSX." });
            var overwriteExisting = bool.TryParse(form["overwriteExisting"], out var overwrite) && overwrite;
            var sheetName = string.IsNullOrWhiteSpace(form["sheetName"]) ? null : form["sheetName"].ToString();
            try
            {
                await using var stream = file.OpenReadStream();
                return Results.Ok(await service.PreviewAsync(stream, file.FileName, overwriteExisting, ct, sheetName));
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Results.BadRequest(new { message = "El archivo XLSX no se pudo leer." }); }
        });
        imports.MapPost("/identification/commit", async (HttpRequest request, IdentificationImportService service, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null) return Results.BadRequest(new { message = "Adjuntá un archivo XLSX." });
            var overwriteExisting = bool.TryParse(form["overwriteExisting"], out var overwrite) && overwrite;
            var confirmOverwrite = bool.TryParse(form["confirmOverwrite"], out var confirmed) && confirmed;
            var sheetName = string.IsNullOrWhiteSpace(form["sheetName"]) ? null : form["sheetName"].ToString();
            try
            {
                await using var stream = file.OpenReadStream();
                var result = await service.CommitAsync(stream, file.FileName, overwriteExisting, confirmOverwrite,
                    UserId(user), user.Identity?.Name, ct, sheetName);
                return result.CanCommit ? Results.Ok(result) : Results.BadRequest(result);
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Results.BadRequest(new { message = "El archivo XLSX no se pudo leer." }); }
        });
        imports.MapGet("/identification/quality", async (IdentificationImportService service, CancellationToken ct) =>
            Results.Ok(await service.GetQualityAsync(ct)));
        imports.MapPost("/passenger-travel/preview", async (HttpRequest request,
            PassengerTravelManifestImportService service, CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null) return Results.BadRequest(new { message = "Adjuntá un archivo CSV o XLSX." });
            var overwriteIdentity = bool.TryParse(form["overwriteExistingIdentity"], out var overwrite) && overwrite;
            var replaceFlights = bool.TryParse(form["replaceConflictingFlightAssignments"], out var replace) && replace;
            try
            {
                var aliases = ParseAliases(form["aliasesJson"]);
                await using var stream = file.OpenReadStream();
                var result = await service.PreviewAsync(stream, file.FileName, overwriteIdentity, replaceFlights, aliases, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (JsonException) { return Results.BadRequest(new { message = "La selección manual de coincidencias no es válida." }); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { return Results.BadRequest(new { message = "El archivo de actualización no se pudo leer." }); }
        });
        imports.MapPost("/passenger-travel/commit", async (HttpRequest request,
            PassengerTravelManifestImportService service, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null) return Results.BadRequest(new { message = "Adjuntá un archivo CSV o XLSX." });
            var overwriteIdentity = bool.TryParse(form["overwriteExistingIdentity"], out var overwrite) && overwrite;
            var replaceFlights = bool.TryParse(form["replaceConflictingFlightAssignments"], out var replace) && replace;
            var confirmed = bool.TryParse(form["confirmAuthoritativeUpdate"], out var authoritative) && authoritative;
            var previewHash = form["previewHash"].ToString();
            try
            {
                var aliases = ParseAliases(form["aliasesJson"]);
                await using var stream = file.OpenReadStream();
                var result = await service.CommitAsync(stream, file.FileName, previewHash, overwriteIdentity,
                    replaceFlights, confirmed, aliases, UserId(user), user.Identity?.Name, ct);
                return result.CanCommit ? Results.Ok(result) : Results.BadRequest(result);
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (JsonException) { return Results.BadRequest(new { message = "La selección manual de coincidencias no es válida." }); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { return Results.BadRequest(new { message = "La actualización no se pudo confirmar." }); }
        });
        imports.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok((await db.ImportRuns.AsNoTracking().ToListAsync(ct)).OrderByDescending(x => x.CreatedAt).Take(50)));
        var exports = api.MapGroup("/exports");
        exports.MapGet("/control.xlsx", async (ExcelExportService service, CancellationToken ct) => Results.File(await service.ExportAsync(ct), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"control-viaje-{DateTime.UtcNow:yyyyMMdd}.xlsx"));
        exports.MapGet("/passengers.csv", async (ExcelExportService service, CancellationToken ct) => Results.File(System.Text.Encoding.UTF8.GetPreamble().Concat(await service.ExportPassengersCsvAsync(ct)).ToArray(), "text/csv; charset=utf-8", $"pasajeros-{DateTime.UtcNow:yyyyMMdd}.csv"));
        exports.MapGet("/pending.xlsx", async (ExcelExportService service, CancellationToken ct) => Results.File(await service.ExportPendingAsync(ct), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"pendientes-{DateTime.UtcNow:yyyyMMdd}.xlsx"));
        static async Task<IResult> StructuredJson(ExcelExportService service, CancellationToken ct) =>
            Results.File(System.Text.Encoding.UTF8.GetBytes(await service.ExportBackupJsonAsync(ct)), "application/json",
                $"travel-control-structured-{DateTime.UtcNow:yyyyMMdd}.json");
        exports.MapGet("/structured.json", StructuredJson).RequireAuthorization("AdminOnly");
        exports.MapGet("/backup.json", StructuredJson).RequireAuthorization("AdminOnly");
    }

    private static void MapAttachments(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/attachments");
        group.MapGet("/", async (Guid? passengerId, Guid? roomId, Guid? flightId, Guid? baggageId,
            AppDbContext db, EvidenceResolver evidenceResolver, CancellationToken ct) =>
        {
            if (passengerId.HasValue && !roomId.HasValue && !flightId.HasValue && !baggageId.HasValue)
            {
                var related = await evidenceResolver.GetPassengerEvidenceAsync(passengerId.Value, ct);
                return Results.Ok(related.Select(item => new
                {
                    Id = item.AttachmentId,
                    item.LinkId,
                    item.EvidenceType,
                    DocumentType = item.EvidenceType,
                    item.SourceType,
                    item.SourceId,
                    item.SourceLabel,
                    item.IsDirect,
                    item.CanUnlinkHere,
                    item.AffectedPassengerCount,
                    item.ManagePath,
                    item.OriginalName,
                    item.MimeType,
                    item.Size,
                    item.UploadedAt
                }));
            }
            var query = db.Attachments.AsNoTracking().AsQueryable();
            if (passengerId.HasValue) query = query.Where(x => x.PassengerId == passengerId || x.Links.Any(link => link.PassengerId == passengerId));
            if (roomId.HasValue) query = query.Where(x => x.RoomReservationId == roomId || x.Links.Any(link => link.RoomReservationId == roomId));
            if (flightId.HasValue) query = query.Where(x => x.FlightBookingId == flightId || x.Links.Any(link => link.FlightBookingId == flightId));
            if (baggageId.HasValue) query = query.Where(x => x.BaggageEntitlementId == baggageId || x.Links.Any(link => link.BaggageEntitlementId == baggageId));
            var entries = await query.Select(x => new
            {
                x.Id,
                LegacyDocumentType = x.DocumentType,
                x.OriginalName,
                x.MimeType,
                x.Size,
                x.UploadedAt,
                x.Description,
                Links = x.Links.Select(link => new
                {
                    link.Id,
                    link.PassengerId,
                    RoomId = link.RoomReservationId,
                    FlightId = link.FlightBookingId,
                    BaggageId = link.BaggageEntitlementId,
                    link.EvidenceType,
                    link.CreatedAt
                })
            }).ToListAsync(ct);
            return Results.Ok(entries.OrderByDescending(x => x.UploadedAt));
        });
        group.MapPost("/", async (HttpRequest request, AttachmentStorage storage, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct); var file = form.Files.GetFile("file"); if (file is null) return Results.BadRequest(new { message = "Falta el archivo." });
            if (!Enum.TryParse<DocumentType>(form["documentType"], true, out var type)) type = DocumentType.Other;
            Guid? Parse(string key) => Guid.TryParse(form[key], out var id) ? id : null;
            try { var stored = await storage.SaveAsync(file, type, UserId(user), form["description"], Parse("passengerId"), Parse("roomId"), Parse("flightId"), Parse("baggageId"), ct);
                if (!stored.DuplicateFile)
                    await Audit(db, user, "Attachment", stored.Link.PassengerId, stored.Entity.Id, "Create", null,
                        new { stored.Entity.DocumentType, stored.Entity.MimeType, stored.Entity.Size }, ct);
                if (stored.LinkCreated)
                    await Audit(db, user, "AttachmentLink", stored.Link.PassengerId, stored.Link.Id, "Create", null,
                        new { stored.Link.AttachmentId, stored.Link.EvidenceType, stored.Link.PassengerId, stored.Link.RoomReservationId, stored.Link.FlightBookingId, stored.Link.BaggageEntitlementId }, ct);
                return Results.Ok(new { AttachmentId = stored.Entity.Id, stored.Entity.OriginalName, stored.Entity.MimeType, stored.Entity.Size,
                    stored.Link.EvidenceType,
                    LinkId = stored.Link.Id, stored.DuplicateFile, stored.LinkCreated }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { message = ex.Message }); }
        }).RequireAuthorization("CanEdit");
        group.MapPost("/{id:guid}/links", async (Guid id, AttachmentLinkRequest req, AttachmentStorage storage,
            AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            try
            {
                if (!req.EvidenceType.HasValue) return Results.BadRequest(new { message = "EvidenceType es obligatorio." });
                var (link, created) = await storage.LinkAsync(id, req.EvidenceType.Value, UserId(user), req.PassengerId, req.RoomId, req.FlightId, req.BaggageId, ct);
                if (created)
                    await Audit(db, user, "AttachmentLink", req.PassengerId, link.Id, "Create", null,
                        new { link.AttachmentId, link.EvidenceType, link.PassengerId, link.RoomReservationId, link.FlightBookingId, link.BaggageEntitlementId }, ct);
                return Results.Ok(new { LinkId = link.Id, link.AttachmentId, link.EvidenceType, linkCreated = created });
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { message = ex.Message }); }
        }).RequireAuthorization("CanEdit");
        group.MapGet("/{attachmentId:guid}/links/{linkId:guid}/impact", async (Guid attachmentId, Guid linkId,
            AttachmentStorage storage, CancellationToken ct) =>
        {
            try { return Results.Ok(await storage.GetImpactAsync(attachmentId, linkId, ct)); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });
        group.MapDelete("/{attachmentId:guid}/links/{linkId:guid}", async (Guid attachmentId, Guid linkId,
            bool? deleteIfOrphan, AttachmentStorage storage, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (deleteIfOrphan == true && !user.IsInRole(nameof(UserRole.Administrator))) return Results.Forbid();
            try
            {
                var result = await storage.UnlinkAsync(attachmentId, linkId, deleteIfOrphan == true,
                    UserId(user), user.Identity?.Name, ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        }).RequireAuthorization("CanEdit");
        group.MapGet("/{id:guid}", async (Guid id, AttachmentStorage storage, CancellationToken ct) =>
        {
            try { var (entity, stream) = await storage.OpenAsync(id, ct); return Results.File(stream, entity.MimeType, entity.OriginalName, enableRangeProcessing: true); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });
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
        group.MapPost("/", async (UserCreateRequest req, UserManager<AppUser> users, AppDbContext db, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var user = new AppUser { UserName = req.Email.Trim(), Email = req.Email.Trim(), DisplayName = req.DisplayName.Trim(), EmailConfirmed = true };
            var result = await users.CreateAsync(user, req.InitialPassword); if (!result.Succeeded) return IdentityErrors(result);
            var roleResult = await users.AddToRoleAsync(user, req.Role.ToString()); if (!roleResult.Succeeded) return IdentityErrors(roleResult);
            await Audit(db, principal, "User", null, user.Id, "Create", null, new { user.Email, user.DisplayName, role = req.Role, user.IsActive }, ct);
            return Results.Created($"/api/users/{user.Id}", new { user.Id });
        });
        group.MapPut("/{id:guid}", async (Guid id, UserUpdateRequest req, UserManager<AppUser> users, AppDbContext db, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var user = await users.FindByIdAsync(id.ToString()); if (user is null) return Results.NotFound();
            var currentUserId = UserId(principal);
            var current = await users.GetRolesAsync(user);
            var isAdministrator = current.Contains(nameof(UserRole.Administrator));
            var activeAdministrators = (await users.GetUsersInRoleAsync(nameof(UserRole.Administrator))).Count(x => x.IsActive);
            string? blockedReason = null;
            if (id == currentUserId && !req.IsActive) blockedReason = "No podés desactivar tu propia cuenta.";
            else if (user.IsActive && isAdministrator && activeAdministrators <= 1
                && (!req.IsActive || req.Role != UserRole.Administrator))
                blockedReason = "La aplicación debe conservar al menos un administrador activo.";
            if (blockedReason is not null)
            {
                await Audit(db, principal, "User", null, id, "ProtectionBlocked", new { user.IsActive, roles = current }, new { requestedActive = req.IsActive, requestedRole = req.Role }, ct);
                return Results.BadRequest(new { message = blockedReason });
            }
            var before = new { user.DisplayName, user.IsActive, roles = current };
            user.DisplayName = req.DisplayName.Trim(); user.IsActive = req.IsActive;
            var update = await users.UpdateAsync(user); if (!update.Succeeded) return IdentityErrors(update);
            if (!current.Contains(req.Role.ToString()) || current.Count != 1)
            {
                var remove = await users.RemoveFromRolesAsync(user, current); if (!remove.Succeeded) return IdentityErrors(remove);
                var add = await users.AddToRoleAsync(user, req.Role.ToString()); if (!add.Succeeded) return IdentityErrors(add);
            }
            await Audit(db, principal, "User", null, id, "Update", before, new { user.DisplayName, user.IsActive, role = req.Role }, ct);
            return Results.NoContent();
        });
        group.MapPost("/{id:guid}/reset-password", async (Guid id, AdminPasswordResetRequest req, UserManager<AppUser> users, AppDbContext db, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var user = await users.FindByIdAsync(id.ToString()); if (user is null) return Results.NotFound();
            var token = await users.GeneratePasswordResetTokenAsync(user); var result = await users.ResetPasswordAsync(user, token, req.NewPassword);
            if (!result.Succeeded) return IdentityErrors(result);
            await Audit(db, principal, "User", null, id, "PasswordReset", null, new { reset = true }, ct);
            return Results.NoContent();
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
        .GroupBy(x => string.IsNullOrWhiteSpace(x.PropertyName)
            ? "request"
            : char.ToLowerInvariant(x.PropertyName[0]) + x.PropertyName[1..])
        .ToDictionary(x => x.Key, x => x.Select(e => e.ErrorMessage).ToArray()));
    private static IResult IdentityErrors(IdentityResult result) => Results.ValidationProblem(result.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray()));
    private static IResult Conflict() => Results.Conflict(new { message = "El registro cambió; recargá la vista antes de guardar." });
    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static string EvidenceSourceLabel(string source) => source switch
    {
        "Direct" => "Vinculado directamente",
        "FlightBooking" => "Compartido por PNR",
        "RoomReservation" => "Compartido por habitación",
        "BaggageEntitlement" => "Compartido por equipaje",
        _ => "Evidencia relacionada"
    };
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static IReadOnlyDictionary<int, Guid> ParseAliases(string? json) => string.IsNullOrWhiteSpace(json)
        ? new Dictionary<int, Guid>()
        : JsonSerializer.Deserialize<Dictionary<int, Guid>>(json) ?? new Dictionary<int, Guid>();
    private static string? Mask(string? value) => string.IsNullOrWhiteSpace(value) ? null : $"***{value[^Math.Min(3, value.Length)..]}";
    private static async Task Audit(AppDbContext db, ClaimsPrincipal user, string entity, Guid? passengerId, object id, string action, object? before, object? after, CancellationToken ct)
    {
        db.AuditLogs.Add(new AuditLog { UserId = UserId(user), UserName = user.Identity?.Name, EntityName = entity, EntityId = id.ToString() ?? "", PassengerId = passengerId,
            Action = action, PreviousValue = before is null ? null : JsonSerializer.Serialize(before), NewValue = after is null ? null : JsonSerializer.Serialize(after) });
        await db.SaveChangesAsync(ct);
    }
}
