using FluentValidation;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using TravelControl.Api.Contracts;
using TravelControl.Api.Data;
using TravelControl.Api.Domain;
using TravelControl.Api.Services;

namespace TravelControl.Api.Endpoints;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapTravelControlApi(this IEndpointRouteBuilder endpoints)
    {
        MapAuth(endpoints);
        var api = endpoints.MapGroup("/api").RequireAuthorization();
        MapDashboard(api); MapPassengers(api); MapRooms(api); MapFlights(api); MapBaggage(api); MapTransfers(api);
        MapFollowUps(api); MapImportsExports(api); MapAttachments(api); MapReference(api); MapAudit(api);
        return endpoints;
    }

    private static void MapAuth(IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/auth");
        auth.MapGet("/csrf", (IAntiforgery antiforgery, HttpContext ctx) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(ctx);
            return Results.Ok(new { token = tokens.RequestToken });
        }).AllowAnonymous();
        auth.MapGet("/setup-status", async (UserManager<AppUser> users) => Results.Ok(new { required = !await users.Users.AnyAsync() })).AllowAnonymous();
        auth.MapPost("/setup", async (SetupRequest request, UserManager<AppUser> users) =>
        {
            if (await users.Users.AnyAsync()) return Results.Conflict(new { message = "La configuración inicial ya fue completada." });
            var user = new AppUser { UserName = request.Email.Trim(), Email = request.Email.Trim(), DisplayName = request.DisplayName.Trim(), EmailConfirmed = true };
            var result = await users.CreateAsync(user, request.Password);
            if (!result.Succeeded) return Results.ValidationProblem(result.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray()));
            await users.AddToRoleAsync(user, nameof(UserRole.Administrator));
            return Results.Created("/api/auth/me", new { message = "Administrador creado." });
        }).AllowAnonymous().RequireRateLimiting("auth");
        auth.MapPost("/login", async (LoginRequest request, SignInManager<AppUser> signIn) =>
        {
            var result = await signIn.PasswordSignInAsync(request.Email.Trim(), request.Password, request.RememberMe, lockoutOnFailure: true);
            return result.Succeeded ? Results.Ok(new { message = "Sesión iniciada." })
                : result.IsLockedOut ? Results.Problem("Cuenta bloqueada temporalmente.", statusCode: 423)
                : Results.Problem("Correo o contraseña incorrectos.", statusCode: 401);
        }).AllowAnonymous().RequireRateLimiting("auth");
        auth.MapPost("/logout", async (SignInManager<AppUser> signIn) => { await signIn.SignOutAsync(); return Results.NoContent(); }).RequireAuthorization();
        auth.MapGet("/me", async (ClaimsPrincipal principal, UserManager<AppUser> users) =>
        {
            var user = await users.GetUserAsync(principal); if (user is null) return Results.Unauthorized();
            return Results.Ok(new { user.Id, user.Email, user.DisplayName, roles = await users.GetRolesAsync(user) });
        }).RequireAuthorization();
    }

    private static void MapDashboard(RouteGroupBuilder api) => api.MapGet("/dashboard", async (string? operatorName, string? overall, string? owner, DashboardService service, CancellationToken ct)
        => Results.Ok(await service.GetAsync(operatorName, overall, owner, ct)));

    private static void MapPassengers(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/passengers");
        group.MapGet("/", async (string? search, string? operatorName, string? overall, string? requirement, string? status,
            int page, int pageSize, PassengerQueryService service, CancellationToken ct) =>
        {
            page = Math.Max(1, page); pageSize = Math.Clamp(pageSize == 0 ? 25 : pageSize, 1, 100);
            var q = service.BaseQuery().AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var normalized = TextNormalizer.Normalize(search);
                q = q.Where(x => x.NormalizedName.Contains(normalized) || (x.NormalizedPassportNumber != null && x.NormalizedPassportNumber.Contains(normalized))
                    || (x.RoomReservation != null && x.RoomReservation.InternalCode.ToUpper().Contains(search.ToUpper()))
                    || x.PassengerFlights.Any(f => (f.FlightBooking.Pnr != null && f.FlightBooking.Pnr.ToUpper().Contains(search.ToUpper())) || (f.ElectronicTicketNumber != null && f.ElectronicTicketNumber.Contains(search))));
            }
            if (!string.IsNullOrWhiteSpace(operatorName)) q = q.Where(x => x.PrimaryOperator != null && x.PrimaryOperator.Name == operatorName);
            var all = await q.OrderBy(x => x.FullName).ToListAsync(ct); var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var mapped = all.Select(x => PassengerQueryService.Map(x, today));
            if (Enum.TryParse<PassengerOverallStatus>(overall, true, out var overallFilter)) mapped = mapped.Where(x => x.OverallStatus == overallFilter);
            if (!string.IsNullOrWhiteSpace(requirement)) mapped = mapped.Where(x =>
            {
                var r = x.Requirements.FirstOrDefault(y => y.Key == requirement);
                return r is not null && (!Enum.TryParse<VerificationStatus>(status, true, out var sf) ? !BusinessRules.IsResolved(r) : r.Status == sf);
            });
            var result = mapped.ToList();
            return Results.Ok(new PagedResult<PassengerListItem>(result.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, result.Count));
        });
        group.MapGet("/{id:guid}", async (Guid id, PassengerQueryService service, CancellationToken ct) =>
        {
            var p = await service.BaseQuery().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return Results.NotFound();
            var dto = new
            {
                p.Id, p.FullName, p.BirthDate, p.Nationality, p.PassportExpiry, p.Phone, p.Email, p.EstimatedHotelArrival,
                p.DietaryRestrictions, p.Notes, p.InternalOwner, p.NextAction, p.NextActionDueDate, p.PassportReviewStatus, p.DocumentationStatus, p.DocumentationExceptionReason, p.Version,
                PrimaryOperator = p.PrimaryOperator is null ? null : new { p.PrimaryOperator.Id, p.PrimaryOperator.Name },
                RoomReservation = p.RoomReservation is null ? null : new { p.RoomReservation.Id, p.RoomReservation.InternalCode, p.RoomReservation.Hotel, p.RoomReservation.RoomType, p.RoomReservation.CheckIn, p.RoomReservation.CheckOut, p.RoomReservation.Status },
                PassengerFlights = p.PassengerFlights.Select(x => new { x.FlightBookingId, x.ElectronicTicketNumber, x.TicketStatus, x.Notes, Booking = new { x.FlightBooking.Pnr, x.FlightBooking.Airline, x.FlightBooking.Status, Segments = x.FlightBooking.Segments.OrderBy(s => s.Sequence).Select(s => new { s.Id, s.Type, s.FlightNumber, s.OriginAirport, s.DestinationAirport, s.DepartureAt, s.ArrivalAt }) } }),
                BaggageEntitlements = p.BaggageEntitlements.Select(x => new { x.Id, x.FlightBookingId, x.Status, x.CheckedBagCount, x.WeightPerBagKg, x.Includes23Kg, x.AppliesOutbound, x.AppliesReturn, x.SourceReference, x.Notes }),
                PassengerTransfers = p.PassengerTransfers.Select(x => new { x.TransferBookingId, Booking = new { x.TransferBooking.Status, x.TransferBooking.Company, x.TransferBooking.VoucherCode, x.TransferBooking.Coverage, x.TransferBooking.ArrivalPickupAt, x.TransferBooking.DeparturePickupAt } }),
                FollowUps = p.FollowUps.Select(x => new { x.Id, x.Title, x.Description, x.Owner, x.DueDate, x.Status, x.Priority })
            };
            return Results.Ok(new { passenger = dto, computed = BusinessRules.CalculatePassenger(p, DateOnly.FromDateTime(DateTime.UtcNow)), maskedPassport = PassengerQueryService.MaskPassport(p.PassportNumber) });
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
            if (await db.Passengers.AnyAsync(x => x.TripId == trip.Id && x.NormalizedName == normalized, ct)) return Results.Conflict(new { message = "Ya existe un pasajero con ese nombre normalizado." });
            var p = new Passenger { TripId = trip.Id, FullName = req.FullName.Trim(), NormalizedName = normalized, BirthDate = req.BirthDate,
                Nationality = req.Nationality, PassportNumber = req.PassportNumber, NormalizedPassportNumber = Blank(TextNormalizer.Normalize(req.PassportNumber)),
                PassportExpiry = req.PassportExpiry, Phone = req.Phone, Email = req.Email, PrimaryOperatorId = req.PrimaryOperatorId,
                RoomReservationId = req.RoomReservationId, InternalOwner = req.InternalOwner, NextAction = req.NextAction,
                NextActionDueDate = req.NextActionDueDate, DietaryRestrictions = req.DietaryRestrictions, Notes = req.Notes, CreatedById = UserId(user), UpdatedById = UserId(user) };
            db.Passengers.Add(p); await db.SaveChangesAsync(ct); await Audit(db, user, "Passenger", p.Id, "Create", null, new { p.FullName }, ct);
            return Results.Created($"/api/passengers/{p.Id}", new { p.Id });
        }).RequireAuthorization("CanEdit");
        group.MapPut("/{id:guid}", async (Guid id, UpdatePassengerRequest req, IValidator<UpdatePassengerRequest> validator, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct); if (!validation.IsValid) return Validation(validation);
            var p = await db.Passengers.FindAsync([id], ct); if (p is null) return Results.NotFound();
            if (p.Version != req.Version) return Results.Conflict(new { message = "El registro fue actualizado por otra persona. Recargá los datos." });
            if (req.DocumentationStatus == VerificationStatus.Confirmed)
            {
                var missing = new List<string>();
                if (req.PassportReviewStatus != VerificationStatus.Confirmed) missing.Add("pasaporte revisado");
                if (!await db.PassengerFlights.AnyAsync(x => x.PassengerId == id && x.TicketStatus == VerificationStatus.Confirmed, ct)) missing.Add("ticket revisado");
                var roomReady = req.RoomReservationId.HasValue && await db.RoomReservations.AnyAsync(x => x.Id == req.RoomReservationId && x.Status == VerificationStatus.Confirmed && x.SourceReference != null && x.SourceReference != "", ct);
                if (!roomReady) missing.Add("voucher o referencia de habitación");
                var transferCoverage = await db.PassengerTransfers.Where(x => x.PassengerId == id && x.TransferBooking.Status == VerificationStatus.Confirmed)
                    .Select(x => x.TransferBooking.Coverage).ToListAsync(ct);
                if (!(transferCoverage.Contains(TransferCoverage.Both) || (transferCoverage.Contains(TransferCoverage.Arrival) && transferCoverage.Contains(TransferCoverage.Departure))))
                    missing.Add("transfer de llegada y salida revisado");
                var hasFlightEvidence = await db.PassengerFlights.AnyAsync(x => x.PassengerId == id && x.FlightBooking.SourceReference != null && x.FlightBooking.SourceReference != "", ct)
                    || await db.Attachments.AnyAsync(x => x.PassengerId == id && x.DocumentType == DocumentType.AirTicket, ct);
                var hasTransferEvidence = await db.PassengerTransfers.AnyAsync(x => x.PassengerId == id && x.TransferBooking.SourceReference != null && x.TransferBooking.SourceReference != "", ct)
                    || await db.Attachments.AnyAsync(x => x.PassengerId == id && x.DocumentType == DocumentType.TransferVoucher, ct);
                if (!hasFlightEvidence) missing.Add("comprobante o referencia de vuelo");
                if (!hasTransferEvidence) missing.Add("comprobante o referencia de transfer");
                if (missing.Count > 0) return Results.BadRequest(new { message = "No se puede confirmar la documentación.", missing });
            }
            var before = new { p.FullName, passport = PassengerQueryService.MaskPassport(p.PassportNumber), p.DocumentationStatus, p.RoomReservationId, p.NextAction };
            p.FullName = req.FullName.Trim(); p.NormalizedName = TextNormalizer.Normalize(req.FullName); p.BirthDate = req.BirthDate; p.Nationality = req.Nationality;
            p.PassportNumber = req.PassportNumber; p.NormalizedPassportNumber = Blank(TextNormalizer.Normalize(req.PassportNumber)); p.PassportExpiry = req.PassportExpiry;
            p.PassportReviewStatus = req.PassportReviewStatus; p.DocumentationStatus = req.DocumentationStatus; p.DocumentationExceptionReason = req.DocumentationExceptionReason;
            if (req.DocumentationStatus == VerificationStatus.Confirmed) { p.DocumentationVerifiedAt = DateTimeOffset.UtcNow; p.DocumentationVerifiedById = UserId(user); }
            p.Phone = req.Phone; p.Email = req.Email; p.PrimaryOperatorId = req.PrimaryOperatorId; p.RoomReservationId = req.RoomReservationId;
            p.EstimatedHotelArrival = req.EstimatedHotelArrival; p.DietaryRestrictions = req.DietaryRestrictions; p.Notes = req.Notes; p.InternalOwner = req.InternalOwner;
            p.NextAction = req.NextAction; p.NextActionDueDate = req.NextActionDueDate; p.UpdatedById = UserId(user);
            try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Results.Conflict(new { message = "Conflicto de edición." }); }
            await Audit(db, user, "Passenger", id, "Update", before, new { p.FullName, passport = PassengerQueryService.MaskPassport(p.PassportNumber), p.DocumentationStatus, p.RoomReservationId, p.NextAction }, ct);
            return Results.NoContent();
        }).RequireAuthorization("CanEdit");
        group.MapPost("/bulk-assign", async (BulkAssignRequest req, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (req.PassengerIds.Count == 0) return Results.BadRequest(new { message = "Seleccioná al menos un pasajero." });
            var records = await db.Passengers.Where(x => req.PassengerIds.Contains(x.Id)).ToListAsync(ct);
            foreach (var p in records)
            {
                if (req.RoomReservationId.HasValue) p.RoomReservationId = req.RoomReservationId;
                if (req.Owner is not null) p.InternalOwner = req.Owner;
                if (req.FlightBookingId.HasValue && !await db.PassengerFlights.AnyAsync(x => x.PassengerId == p.Id && x.FlightBookingId == req.FlightBookingId, ct))
                    db.PassengerFlights.Add(new PassengerFlight { PassengerId = p.Id, FlightBookingId = req.FlightBookingId.Value });
                if (req.TransferBookingId.HasValue && !await db.PassengerTransfers.AnyAsync(x => x.PassengerId == p.Id && x.TransferBookingId == req.TransferBookingId, ct))
                    db.PassengerTransfers.Add(new PassengerTransfer { PassengerId = p.Id, TransferBookingId = req.TransferBookingId.Value });
            }
            await db.SaveChangesAsync(ct); await Audit(db, user, "Passenger", Guid.Empty, "BulkAssign", null, new { count = records.Count, req.RoomReservationId, req.Owner, req.FlightBookingId, req.TransferBookingId }, ct);
            return Results.Ok(new { updated = records.Count });
        }).RequireAuthorization("CanEdit");
    }

    private static void MapRooms(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/rooms");
        group.MapGet("/", async (string? operatorName, string? status, AppDbContext db, CancellationToken ct) =>
        {
            var q = db.RoomReservations.AsNoTracking().Include(x => x.Operator).Include(x => x.Passengers).AsQueryable();
            if (!string.IsNullOrWhiteSpace(operatorName)) q = q.Where(x => x.Operator.Name == operatorName);
            if (Enum.TryParse<VerificationStatus>(status, true, out var sf)) q = q.Where(x => x.Status == sf);
            var rooms = await q.OrderBy(x => x.InternalCode).ToListAsync(ct);
            return Results.Ok(rooms.Select(x => new { x.Id, x.InternalCode, Operator = x.Operator.Name, x.Status, x.Hotel, x.RoomType, x.CheckIn, x.CheckOut, x.Nights,
                Occupants = x.Passengers.Select(p => new { p.Id, p.FullName }), x.ExpectedCapacity, x.SourceReference, x.HotelReservationNumber, x.OperatorContact, x.Notes,
                Alerts = new[] { x.SpecificPropertyPending ? BusinessRules.TopTravelPropertyAlert : null, !x.CapacityOverride && x.Passengers.Count > x.ExpectedCapacity ? "Ocupación incompatible" : null }.Where(a => a != null), x.Version }));
        });
        group.MapPut("/{id:guid}", async (Guid id, RoomUpdateRequest req, IValidator<RoomUpdateRequest> validator, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct); if (!validation.IsValid) return Validation(validation);
            var room = await db.RoomReservations.Include(x => x.Passengers).SingleOrDefaultAsync(x => x.Id == id, ct); if (room is null) return Results.NotFound();
            if (room.Version != req.Version) return Results.Conflict(new { message = "La habitación cambió; recargá la vista." });
            if (req.Status == VerificationStatus.Confirmed && !req.CapacityOverride && room.Passengers.Count > req.ExpectedCapacity)
                return Results.BadRequest(new { message = "La ocupación supera la capacidad esperada.", occupants = room.Passengers.Count, capacity = req.ExpectedCapacity });
            var before = new { room.Status, room.Hotel, room.RoomType, room.CheckIn, room.CheckOut };
            room.InternalCode = req.InternalCode; room.OperatorId = req.OperatorId; room.Status = req.Status; room.Hotel = req.Hotel; room.RoomType = req.RoomType;
            room.ExpectedCapacity = req.ExpectedCapacity; room.CapacityOverride = req.CapacityOverride; room.CapacityOverrideReason = req.CapacityOverrideReason;
            room.CheckIn = req.CheckIn; room.CheckOut = req.CheckOut; room.HotelReservationNumber = req.HotelReservationNumber; room.MealPlan = req.MealPlan;
            room.SourceReference = req.SourceReference; room.OperatorContact = req.OperatorContact; room.Notes = req.Notes;
            room.SpecificPropertyPending = (await db.Operators.Where(x => x.Id == req.OperatorId).Select(x => x.Name).SingleAsync(ct)) == "Top Travel"
                && (req.Hotel is null || TextNormalizer.Normalize(req.Hotel).Contains("PROPIEDAD EXACTA"));
            if (req.Status == VerificationStatus.Confirmed) { room.VerifiedAt = DateTimeOffset.UtcNow; room.VerifiedById = UserId(user); }
            await db.SaveChangesAsync(ct); await Audit(db, user, "RoomReservation", id, "Update", before, new { room.Status, room.Hotel, room.RoomType, room.CheckIn, room.CheckOut }, ct);
            return Results.NoContent();
        }).RequireAuthorization("CanEdit");
    }

    private static void MapFlights(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/flight-bookings");
        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var data = await db.FlightBookings.AsNoTracking().Include(x => x.Segments).Include(x => x.PassengerFlights).ThenInclude(x => x.Passenger).OrderBy(x => x.Pnr).ToListAsync(ct);
            return Results.Ok(data.Select(x => new { x.Id, x.Status, x.Airline, x.IssuingAgency, x.Pnr, x.GeneralReference, x.SourceReference, x.VerifiedAt, x.Notes,
                Segments = x.Segments.OrderBy(s => s.Sequence).Select(s => new { s.Id, s.Type, s.FlightNumber, s.OriginAirport, s.DestinationAirport, s.DepartureAt, s.ArrivalAt, s.OriginTimeZone, s.DestinationTimeZone, s.Sequence }),
                PassengerFlights = x.PassengerFlights.Select(link => new { link.PassengerId, link.ElectronicTicketNumber, link.TicketStatus, link.Notes, Passenger = new { link.Passenger.Id, link.Passenger.FullName } }) }));
        });
        group.MapPost("/", async (FlightBookingRequest req, IValidator<FlightBookingRequest> validator, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct); if (!validation.IsValid) return Validation(validation);
            var tripId = await db.Trips.Where(x => x.IsActive).Select(x => x.Id).SingleAsync(ct);
            var entity = new FlightBooking { TripId = tripId, Status = req.Status == VerificationStatus.Confirmed ? VerificationStatus.ToVerify : req.Status,
                Airline = req.Airline, IssuingAgency = req.IssuingAgency, Pnr = req.Pnr, GeneralReference = req.GeneralReference, SourceReference = req.SourceReference, Notes = req.Notes };
            entity.Segments = req.Segments.Select(s => new FlightSegment { Type = s.Type, FlightNumber = s.FlightNumber, OriginAirport = s.OriginAirport,
                DestinationAirport = s.DestinationAirport, DepartureAt = s.DepartureAt, ArrivalAt = s.ArrivalAt, OriginTimeZone = s.OriginTimeZone, DestinationTimeZone = s.DestinationTimeZone, Sequence = s.Sequence }).ToList();
            entity.PassengerFlights = req.PassengerIds.Distinct().Select(id => new PassengerFlight { PassengerId = id, TicketStatus = VerificationStatus.ToVerify }).ToList();
            db.FlightBookings.Add(entity); await db.SaveChangesAsync(ct); await Audit(db, user, "FlightBooking", entity.Id, "Create", null, new { entity.Pnr, passengers = req.PassengerIds.Count }, ct);
            return Results.Created($"/api/flight-bookings/{entity.Id}", new { entity.Id, warning = req.Status == VerificationStatus.Confirmed ? "Cada pasajero requiere número de ticket antes de confirmar." : null });
        }).RequireAuthorization("CanEdit");
        group.MapPut("/{id:guid}/tickets/{passengerId:guid}", async (Guid id, Guid passengerId, PassengerTicketRequest req, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var link = await db.PassengerFlights.Include(x => x.FlightBooking).ThenInclude(x => x.Segments).SingleOrDefaultAsync(x => x.FlightBookingId == id && x.PassengerId == passengerId, ct);
            if (link is null) return Results.NotFound();
            link.ElectronicTicketNumber = req.ElectronicTicketNumber; link.Notes = req.Notes;
            if (req.Status == VerificationStatus.Confirmed)
            {
                link.FlightBooking.VerifiedAt ??= DateTimeOffset.UtcNow; link.FlightBooking.VerifiedById ??= UserId(user);
                if (!BusinessRules.FlightCanBeConfirmed(link.FlightBooking, link, out var missing)) return Results.BadRequest(new { message = "No se puede confirmar el ticket.", missing });
            }
            link.TicketStatus = req.Status; await db.SaveChangesAsync(ct); await Audit(db, user, "PassengerFlight", passengerId, "UpdateTicket", null, new { bookingId = id, req.Status }, ct);
            return Results.NoContent();
        }).RequireAuthorization("CanEdit");
    }

    private static void MapBaggage(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/baggage");
        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var data = await db.BaggageEntitlements.AsNoTracking().Include(x => x.Passenger).Include(x => x.FlightBooking).OrderBy(x => x.Passenger.FullName).ToListAsync(ct);
            return Results.Ok(data.Select(x => new { x.Id, x.Status, x.CheckedBagCount, x.WeightPerBagKg, x.Includes23Kg, x.AppliesOutbound, x.AppliesReturn, x.SourceReference, x.Notes,
                Passenger = new { x.Passenger.Id, x.Passenger.FullName }, FlightBooking = x.FlightBooking is null ? null : new { x.FlightBooking.Id, x.FlightBooking.Pnr } }));
        });
        group.MapPost("/", async (BaggageUpdateRequest req, IValidator<BaggageUpdateRequest> validator, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct); if (!validation.IsValid) return Validation(validation);
            var entity = await db.BaggageEntitlements.FirstOrDefaultAsync(x => x.PassengerId == req.PassengerId && x.FlightBookingId == req.FlightBookingId, ct)
                ?? new BaggageEntitlement { PassengerId = req.PassengerId, FlightBookingId = req.FlightBookingId };
            if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
            entity.Status = req.Status; entity.CheckedBagCount = req.CheckedBagCount; entity.WeightPerBagKg = req.WeightPerBagKg;
            entity.Includes23Kg = req.CheckedBagCount > 0 && req.WeightPerBagKg >= 23; entity.AppliesOutbound = req.AppliesOutbound; entity.AppliesReturn = req.AppliesReturn;
            entity.ExceptionReason = req.ExceptionReason; entity.SourceReference = req.SourceReference; entity.Notes = req.Notes;
            var hasTicket = req.FlightBookingId.HasValue && await db.PassengerFlights.AnyAsync(x => x.PassengerId == req.PassengerId && x.FlightBookingId == req.FlightBookingId && x.TicketStatus == VerificationStatus.Confirmed, ct);
            if (req.Status == VerificationStatus.Confirmed)
            {
                entity.VerifiedAt = DateTimeOffset.UtcNow; entity.VerifiedById = UserId(user);
                if (!BusinessRules.BaggageCanBeConfirmed(entity, hasTicket, out var missing)) return Results.BadRequest(new { message = "No se puede confirmar la maleta.", missing });
            }
            if (db.Entry(entity).State == EntityState.Detached) db.BaggageEntitlements.Add(entity);
            await db.SaveChangesAsync(ct); await Audit(db, user, "BaggageEntitlement", entity.Id, "Upsert", null, new { req.PassengerId, req.Status, req.CheckedBagCount, req.WeightPerBagKg }, ct);
            return Results.Ok(new { entity.Id });
        }).RequireAuthorization("CanEdit");
    }

    private static void MapTransfers(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/transfers");
        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var data = await db.TransferBookings.AsNoTracking().Include(x => x.PassengerTransfers).ThenInclude(x => x.Passenger).OrderBy(x => x.ArrivalPickupAt).ToListAsync(ct);
            return Results.Ok(data.Select(x => new { x.Id, x.Status, x.Company, x.VoucherCode, x.Contact, x.Airport, x.Hotel, x.Coverage, x.ArrivalPickupAt, x.DeparturePickupAt, x.SourceReference, x.Notes, x.VerifiedAt,
                PassengerTransfers = x.PassengerTransfers.Select(link => new { link.PassengerId, Passenger = new { link.Passenger.Id, link.Passenger.FullName } }) }));
        });
        group.MapPost("/", async (TransferRequest req, IValidator<TransferRequest> validator, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct); if (!validation.IsValid) return Validation(validation);
            var tripId = await db.Trips.Where(x => x.IsActive).Select(x => x.Id).SingleAsync(ct);
            var entity = new TransferBooking { TripId = tripId, Status = req.Status, Company = req.Company, VoucherCode = req.VoucherCode, Contact = req.Contact,
                Airport = req.Airport, Hotel = req.Hotel, Coverage = req.Coverage, ArrivalPickupAt = req.ArrivalPickupAt, DeparturePickupAt = req.DeparturePickupAt,
                SourceReference = req.SourceReference, Notes = req.Notes, PassengerTransfers = req.PassengerIds.Distinct().Select(x => new PassengerTransfer { PassengerId = x }).ToList() };
            if (req.Status == VerificationStatus.Confirmed) { entity.VerifiedAt = DateTimeOffset.UtcNow; entity.VerifiedById = UserId(user); }
            db.TransferBookings.Add(entity); await db.SaveChangesAsync(ct);
            if (req.Status == VerificationStatus.Confirmed && !BusinessRules.TransferCanBeConfirmed(entity, out var missing)) return Results.BadRequest(new { message = "No se puede confirmar el transfer.", missing });
            await Audit(db, user, "TransferBooking", entity.Id, "Create", null, new { req.Company, req.Coverage, passengers = req.PassengerIds.Count }, ct);
            return Results.Created($"/api/transfers/{entity.Id}", new { entity.Id });
        }).RequireAuthorization("CanEdit");
    }

    private static void MapFollowUps(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/follow-ups");
        group.MapGet("/", async (string? owner, string? status, AppDbContext db, CancellationToken ct) =>
        {
            var q = db.FollowUps.AsNoTracking().Include(x => x.Passenger).Include(x => x.RoomReservation).AsQueryable();
            if (!string.IsNullOrWhiteSpace(owner)) q = q.Where(x => x.Owner == owner);
            if (Enum.TryParse<FollowUpStatus>(status, true, out var sf)) q = q.Where(x => x.Status == sf);
            return Results.Ok(await q.OrderByDescending(x => x.Priority).ThenBy(x => x.DueDate).ToListAsync(ct));
        });
        group.MapPost("/", async (FollowUpRequest req, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title) || (!req.PassengerId.HasValue && !req.RoomReservationId.HasValue)) return Results.BadRequest(new { message = "Indicá título y pasajero o habitación." });
            var entity = new FollowUp { PassengerId = req.PassengerId, RoomReservationId = req.RoomReservationId, Title = req.Title.Trim(), Description = req.Description,
                Owner = req.Owner, DueDate = req.DueDate, Status = req.Status, Priority = req.Priority };
            db.FollowUps.Add(entity); await db.SaveChangesAsync(ct); await Audit(db, user, "FollowUp", entity.Id, "Create", null, new { entity.Title, entity.Owner, entity.DueDate }, ct);
            return Results.Created($"/api/follow-ups/{entity.Id}", new { entity.Id });
        }).RequireAuthorization("CanEdit");
    }

    private static void MapImportsExports(RouteGroupBuilder api)
    {
        var imports = api.MapGroup("/imports").RequireAuthorization("AdminOnly");
        imports.MapPost("/preview", async (HttpRequest request, ExcelImportService service, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct); var file = form.Files.GetFile("file");
            if (file is null) return Results.BadRequest(new { message = "Adjuntá un archivo XLSX." });
            await using var stream = file.OpenReadStream(); return Results.Ok(await service.ProcessAsync(stream, file.FileName, true, UserId(user), ct));
        });
        imports.MapPost("/commit", async (HttpRequest request, ExcelImportService service, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct); var file = form.Files.GetFile("file");
            if (file is null) return Results.BadRequest(new { message = "Adjuntá un archivo XLSX." });
            await using var stream = file.OpenReadStream(); var result = await service.ProcessAsync(stream, file.FileName, false, UserId(user), ct);
            return result.CanCommit ? Results.Ok(result) : Results.BadRequest(result);
        });
        imports.MapGet("/", async (AppDbContext db, CancellationToken ct) => Results.Ok(await db.ImportRuns.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(50).ToListAsync(ct)));
        var exports = api.MapGroup("/exports");
        exports.MapGet("/control.xlsx", async (ExcelExportService service, CancellationToken ct) => Results.File(await service.ExportAsync(ct), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"control-viaje-{DateTime.UtcNow:yyyyMMdd}.xlsx"));
        exports.MapGet("/passengers.csv", async (ExcelExportService service, CancellationToken ct) => Results.File(System.Text.Encoding.UTF8.GetPreamble().Concat(await service.ExportPassengersCsvAsync(ct)).ToArray(), "text/csv; charset=utf-8", $"pasajeros-{DateTime.UtcNow:yyyyMMdd}.csv"));
        exports.MapGet("/pending.xlsx", async (ExcelExportService service, CancellationToken ct) => Results.File(await service.ExportPendingAsync(ct), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"pendientes-{DateTime.UtcNow:yyyyMMdd}.xlsx"));
        exports.MapGet("/backup.json", async (ExcelExportService service, CancellationToken ct) => Results.Text(await service.ExportBackupJsonAsync(ct), "application/json")).RequireAuthorization("AdminOnly");
    }

    private static void MapAttachments(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/attachments");
        group.MapGet("/", async (Guid? passengerId, Guid? roomId, Guid? flightId, Guid? baggageId, Guid? transferId, AppDbContext db, CancellationToken ct) =>
        {
            var q = db.Attachments.AsNoTracking().AsQueryable();
            if (passengerId.HasValue) q = q.Where(x => x.PassengerId == passengerId);
            if (roomId.HasValue) q = q.Where(x => x.RoomReservationId == roomId);
            if (flightId.HasValue) q = q.Where(x => x.FlightBookingId == flightId);
            if (baggageId.HasValue) q = q.Where(x => x.BaggageEntitlementId == baggageId);
            if (transferId.HasValue) q = q.Where(x => x.TransferBookingId == transferId);
            return Results.Ok(await q.OrderByDescending(x => x.UploadedAt).Select(x => new { x.Id, x.DocumentType, x.OriginalName, x.MimeType, x.Size, x.UploadedAt, x.Description }).ToListAsync(ct));
        });
        group.MapPost("/", async (HttpRequest request, AttachmentStorage storage, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct); var file = form.Files.GetFile("file"); if (file is null) return Results.BadRequest(new { message = "Falta el archivo." });
            if (!Enum.TryParse<DocumentType>(form["documentType"], true, out var type)) type = DocumentType.Other;
            Guid? Parse(string key) => Guid.TryParse(form[key], out var id) ? id : null;
            try
            {
                var stored = await storage.SaveAsync(file, type, UserId(user), form["description"], Parse("passengerId"), Parse("roomId"), Parse("flightId"), Parse("baggageId"), Parse("transferId"), ct);
                return Results.Ok(new { stored.Entity.Id, stored.Entity.OriginalName, stored.Entity.MimeType, stored.Entity.Size, stored.Duplicate });
            }
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
            var path = entity.SecurePath; db.Attachments.Remove(entity); await db.SaveChangesAsync(ct);
            if (File.Exists(path)) File.Delete(path); await Audit(db, user, "Attachment", id, "Delete", new { entity.OriginalName, entity.Sha256 }, null, ct); return Results.NoContent();
        }).RequireAuthorization("AdminOnly");
    }

    private static void MapReference(RouteGroupBuilder api)
    {
        api.MapGet("/trips", async (AppDbContext db, CancellationToken ct) => Results.Ok(await db.Trips.AsNoTracking().ToListAsync(ct)));
        api.MapGet("/operators", async (AppDbContext db, CancellationToken ct) => Results.Ok(await db.Operators.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct)));
    }

    private static void MapAudit(RouteGroupBuilder api) => api.MapGet("/audit", async (int page, int pageSize, AppDbContext db, CancellationToken ct) =>
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize == 0 ? 50 : pageSize, 1, 100); var q = db.AuditLogs.AsNoTracking().OrderByDescending(x => x.At);
        return Results.Ok(new PagedResult<AuditLog>(await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct), page, pageSize, await q.CountAsync(ct)));
    }).RequireAuthorization("AdminOnly");

    private static IResult Validation(FluentValidation.Results.ValidationResult result) => Results.ValidationProblem(result.Errors
        .GroupBy(x => char.ToLowerInvariant(x.PropertyName[0]) + x.PropertyName[1..]).ToDictionary(x => x.Key, x => x.Select(e => e.ErrorMessage).ToArray()));
    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static async Task Audit(AppDbContext db, ClaimsPrincipal user, string entity, object id, string action, object? before, object? after, CancellationToken ct)
    {
        db.AuditLogs.Add(new AuditLog { UserId = UserId(user), UserName = user.Identity?.Name, EntityName = entity, EntityId = id.ToString() ?? "", Action = action,
            PreviousValue = before is null ? null : JsonSerializer.Serialize(before), NewValue = after is null ? null : JsonSerializer.Serialize(after) });
        await db.SaveChangesAsync(ct);
    }
}
