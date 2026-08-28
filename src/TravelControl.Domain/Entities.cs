namespace TravelControl.Domain;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public long Version { get; set; } = 1;
}

public sealed class Trip : Entity
{
    public required string Name { get; set; }
    public required string Destination { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly WeddingDate { get; set; }
    public string TimeZone { get; set; } = "America/Cancun";
    public bool IsActive { get; set; } = true;
    public int PassportWarningDays { get; set; } = 180;
    public TripTransferStatus TransferStatus { get; set; } = null!;
    public ICollection<Passenger> Passengers { get; set; } = [];
}

public sealed class TripTransferStatus : Entity
{
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;
    public bool IsConfirmed { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? Notes { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

public sealed class Operator : Entity
{
    public required string Name { get; set; }
    public OperatorType Type { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? ContactPerson { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Passenger : Entity
{
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;
    public required string FullName { get; set; }
    public required string NormalizedName { get; set; }
    public string? FirstNames { get; set; }
    public string? LastNames { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? Nationality { get; set; }
    public string? PassportNumber { get; set; }
    public string? NormalizedPassportNumber { get; set; }
    public DateOnly? PassportExpiry { get; set; }
    public VerificationStatus PassportReviewStatus { get; set; } = VerificationStatus.ToVerify;
    public VerificationStatus DocumentationStatus { get; set; } = VerificationStatus.ToVerify;
    public string? DocumentationExceptionReason { get; set; }
    public DateTimeOffset? DocumentationVerifiedAt { get; set; }
    public Guid? DocumentationVerifiedById { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public Guid? PrimaryOperatorId { get; set; }
    public Operator? PrimaryOperator { get; set; }
    public Guid? RoomReservationId { get; set; }
    public RoomReservation? RoomReservation { get; set; }
    public string? EstimatedHotelArrival { get; set; }
    public string? DietaryRestrictions { get; set; }
    public string? Notes { get; set; }
    public string? NextAction { get; set; }
    public DateOnly? NextActionDueDate { get; set; }
    public Guid? CreatedById { get; set; }
    public Guid? UpdatedById { get; set; }
    public ICollection<PassengerFlight> PassengerFlights { get; set; } = [];
    public ICollection<BaggageEntitlement> BaggageEntitlements { get; set; } = [];
    public ICollection<FollowUp> FollowUps { get; set; } = [];
}

public sealed class RoomReservation : Entity
{
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;
    public required string InternalCode { get; set; }
    public Guid OperatorId { get; set; }
    public Operator Operator { get; set; } = null!;
    public VerificationStatus Status { get; set; } = VerificationStatus.ToVerify;
    public string? Hotel { get; set; }
    public string? RoomType { get; set; }
    public int ExpectedCapacity { get; set; }
    public bool CapacityOverride { get; set; }
    public string? CapacityOverrideReason { get; set; }
    public DateOnly? CheckIn { get; set; }
    public DateOnly? CheckOut { get; set; }
    public string? HotelReservationNumber { get; set; }
    public string? MealPlan { get; set; }
    public string? SourceReference { get; set; }
    public string? OperatorContact { get; set; }
    public string? Notes { get; set; }
    public bool SpecificPropertyPending { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public Guid? VerifiedById { get; set; }
    public ICollection<Passenger> Passengers { get; set; } = [];
    public int? Nights => CheckIn.HasValue && CheckOut.HasValue ? CheckOut.Value.DayNumber - CheckIn.Value.DayNumber : null;
}

public sealed class FlightBooking : Entity
{
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;
    public VerificationStatus Status { get; set; } = VerificationStatus.ToVerify;
    public string? Airline { get; set; }
    public string? IssuingAgency { get; set; }
    public string? Pnr { get; set; }
    public string? GeneralReference { get; set; }
    public string? SourceReference { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public Guid? VerifiedById { get; set; }
    public string? Notes { get; set; }
    public ICollection<FlightSegment> Segments { get; set; } = [];
    public ICollection<PassengerFlight> PassengerFlights { get; set; } = [];
}

public sealed class FlightSegment : Entity
{
    public Guid FlightBookingId { get; set; }
    public FlightBooking FlightBooking { get; set; } = null!;
    public SegmentType Type { get; set; }
    public string? FlightNumber { get; set; }
    public string? OriginAirport { get; set; }
    public string? DestinationAirport { get; set; }
    public DateTimeOffset? DepartureAt { get; set; }
    public DateTimeOffset? ArrivalAt { get; set; }
    public string? OriginTimeZone { get; set; }
    public string? DestinationTimeZone { get; set; }
    public int Sequence { get; set; }
}

public sealed class PassengerFlight
{
    public Guid PassengerId { get; set; }
    public Passenger Passenger { get; set; } = null!;
    public Guid FlightBookingId { get; set; }
    public FlightBooking FlightBooking { get; set; } = null!;
    public string? ElectronicTicketNumber { get; set; }
    public VerificationStatus TicketStatus { get; set; } = VerificationStatus.ToVerify;
    public string? Notes { get; set; }
}

public sealed class BaggageEntitlement : Entity
{
    public Guid PassengerId { get; set; }
    public Passenger Passenger { get; set; } = null!;
    public Guid? FlightBookingId { get; set; }
    public FlightBooking? FlightBooking { get; set; }
    public VerificationStatus Status { get; set; } = VerificationStatus.ToVerify;
    public int CheckedBagCount { get; set; }
    public decimal WeightPerBagKg { get; set; }
    public bool AppliesOutbound { get; set; }
    public bool AppliesReturn { get; set; }
    public string? ExceptionReason { get; set; }
    public string? SourceReference { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public Guid? VerifiedById { get; set; }
    public string? Notes { get; set; }
    public bool Includes23Kg => CheckedBagCount >= 1 && WeightPerBagKg >= 23 && AppliesOutbound && AppliesReturn;
}

public sealed class Attachment : Entity
{
    public DocumentType DocumentType { get; set; }
    public required string OriginalName { get; set; }
    public required string StoredName { get; set; }
    public required string MimeType { get; set; }
    public long Size { get; set; }
    public required string SecurePath { get; set; }
    public required string Sha256 { get; set; }
    public Guid UploadedById { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Description { get; set; }
    public Guid? PassengerId { get; set; }
    public Guid? RoomReservationId { get; set; }
    public Guid? FlightBookingId { get; set; }
    public Guid? BaggageEntitlementId { get; set; }
}

public sealed class FollowUp : Entity
{
    public Guid? TripId { get; set; }
    public Guid? PassengerId { get; set; }
    public Passenger? Passenger { get; set; }
    public Guid? RoomReservationId { get; set; }
    public RoomReservation? RoomReservation { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DateOnly? DueDate { get; set; }
    public FollowUpStatus Status { get; set; }
    public FollowUpPriority Priority { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ClosedByUserId { get; set; }
}

public sealed class AuditLog
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public required string EntityName { get; set; }
    public required string EntityId { get; set; }
    public Guid? PassengerId { get; set; }
    public required string Action { get; set; }
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public string? IpContext { get; set; }
}

public sealed class ImportRun : Entity
{
    public required string FileName { get; set; }
    public required string Sha256 { get; set; }
    public bool DryRun { get; set; }
    public required string Status { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Errors { get; set; }
    public string SummaryJson { get; set; } = "{}";
    public Guid? UserId { get; set; }
}
