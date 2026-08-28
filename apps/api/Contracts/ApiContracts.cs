using TravelControl.Api.Domain;

namespace TravelControl.Api.Contracts;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
public sealed record PassengerListItem(
    Guid Id, string FullName, string MaskedPassport, PassportStatus PassportStatus,
    string? Operator, string? RoomCode, string? Hotel, string? RoomType, DateOnly? CheckIn, DateOnly? CheckOut, int? Nights,
    VerificationStatus DocumentationStatus, PassengerOverallStatus OverallStatus, int ProgressPercent,
    IReadOnlyList<Services.RequirementState> Requirements, IReadOnlyList<string> Alerts,
    string? NextAction, string? Owner, DateOnly? NextActionDueDate, DateTimeOffset UpdatedAt, uint Version);
public sealed record DashboardKpi(string Key, string Label, int Value, int Total, int Percent, string Filter);
public sealed record CategoryProgress(string Key, string Label, int Confirmed, int Pending, int InProgress, int NotIncluded, int NotApplicable, int ResolvedPercent);
public sealed record OperatorSummary(string Name, int Rooms, int Passengers, int ConfirmedRooms, IReadOnlyList<string> Alerts);
public sealed record PriorityAction(string Severity, string Title, int Count, string Filter);
public sealed record RecentActivity(long Id, string? Passenger, string Field, string? User, DateTimeOffset At, string? Previous, string? Current);
public sealed record DashboardResponse(IReadOnlyList<DashboardKpi> Kpis, IReadOnlyList<CategoryProgress> Categories,
    IReadOnlyDictionary<string, int> OverallDistribution, IReadOnlyList<OperatorSummary> Operators,
    IReadOnlyList<PriorityAction> PriorityActions, IReadOnlyList<RecentActivity> RecentActivity);

public sealed record CreatePassengerRequest(string FullName, DateOnly? BirthDate, string? Nationality, string? PassportNumber,
    DateOnly? PassportExpiry, string? Phone, string? Email, Guid? PrimaryOperatorId, Guid? RoomReservationId,
    string? InternalOwner, string? NextAction, DateOnly? NextActionDueDate, string? DietaryRestrictions, string? Notes);
public sealed record UpdatePassengerRequest(string FullName, DateOnly? BirthDate, string? Nationality, string? PassportNumber,
    DateOnly? PassportExpiry, VerificationStatus PassportReviewStatus, VerificationStatus DocumentationStatus,
    string? DocumentationExceptionReason, string? Phone, string? Email, Guid? PrimaryOperatorId, Guid? RoomReservationId,
    string? EstimatedHotelArrival, string? DietaryRestrictions, string? Notes, string? InternalOwner,
    string? NextAction, DateOnly? NextActionDueDate, uint Version);
public sealed record BulkAssignRequest(IReadOnlyList<Guid> PassengerIds, Guid? RoomReservationId, string? Owner, Guid? FlightBookingId, Guid? TransferBookingId);
public sealed record SetupRequest(string Email, string Password, string DisplayName);
public sealed record LoginRequest(string Email, string Password, bool RememberMe);
public sealed record RoomUpdateRequest(string InternalCode, Guid OperatorId, VerificationStatus Status, string? Hotel, string? RoomType,
    int ExpectedCapacity, bool CapacityOverride, string? CapacityOverrideReason, DateOnly? CheckIn, DateOnly? CheckOut,
    string? HotelReservationNumber, string? MealPlan, string? SourceReference, string? OperatorContact, string? Notes, uint Version);
public sealed record FlightBookingRequest(VerificationStatus Status, string? Airline, string? IssuingAgency, string? Pnr,
    string? GeneralReference, string? SourceReference, string? Notes, IReadOnlyList<FlightSegmentRequest> Segments, IReadOnlyList<Guid> PassengerIds);
public sealed record FlightSegmentRequest(SegmentType Type, string? FlightNumber, string? OriginAirport, string? DestinationAirport,
    DateTimeOffset? DepartureAt, DateTimeOffset? ArrivalAt, string? OriginTimeZone, string? DestinationTimeZone, int Sequence);
public sealed record PassengerTicketRequest(string ElectronicTicketNumber, VerificationStatus Status, string? Notes);
public sealed record BaggageUpdateRequest(Guid PassengerId, Guid? FlightBookingId, VerificationStatus Status, int CheckedBagCount,
    decimal WeightPerBagKg, bool AppliesOutbound, bool AppliesReturn, string? ExceptionReason, string? SourceReference, string? Notes);
public sealed record TransferRequest(VerificationStatus Status, string? Company, string? VoucherCode, string? Contact,
    string? Airport, string? Hotel, TransferCoverage Coverage, DateTimeOffset? ArrivalPickupAt, DateTimeOffset? DeparturePickupAt,
    string? SourceReference, string? Notes, IReadOnlyList<Guid> PassengerIds);
public sealed record FollowUpRequest(Guid? PassengerId, Guid? RoomReservationId, string Title, string? Description,
    string? Owner, DateOnly? DueDate, FollowUpStatus Status, FollowUpPriority Priority);
