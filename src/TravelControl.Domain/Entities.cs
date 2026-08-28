using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace TravelControl.Api.Domain;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Timestamp] public uint Version { get; set; }
}

public sealed class AppUser : IdentityUser<Guid>
{
    [MaxLength(160)] public string DisplayName { get; set; } = "";
}

public sealed class Trip : Entity
{
    [MaxLength(160)] public required string Name { get; set; }
    [MaxLength(160)] public required string Destination { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly WeddingDate { get; set; }
    [MaxLength(80)] public string TimeZone { get; set; } = "America/Cancun";
    public bool IsActive { get; set; } = true;
    public int PassportWarningDays { get; set; } = 180;
    public ICollection<Passenger> Passengers { get; set; } = [];
}

public sealed class Operator : Entity
{
    [MaxLength(120)] public required string Name { get; set; }
    public OperatorType Type { get; set; }
    [MaxLength(80)] public string? Phone { get; set; }
    [MaxLength(160)] public string? Email { get; set; }
    [MaxLength(160)] public string? ContactPerson { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Passenger : Entity
{
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;
    [MaxLength(220)] public required string FullName { get; set; }
    [MaxLength(220)] public required string NormalizedName { get; set; }
    [MaxLength(100)] public string? FirstNames { get; set; }
    [MaxLength(120)] public string? LastNames { get; set; }
    public DateOnly? BirthDate { get; set; }
    [MaxLength(80)] public string? Nationality { get; set; }
    [MaxLength(40)] public string? PassportNumber { get; set; }
    [MaxLength(40)] public string? NormalizedPassportNumber { get; set; }
    public DateOnly? PassportExpiry { get; set; }
    public VerificationStatus PassportReviewStatus { get; set; } = VerificationStatus.ToVerify;
    public VerificationStatus DocumentationStatus { get; set; } = VerificationStatus.ToVerify;
    [MaxLength(500)] public string? DocumentationExceptionReason { get; set; }
    public DateTimeOffset? DocumentationVerifiedAt { get; set; }
    public Guid? DocumentationVerifiedById { get; set; }
    [MaxLength(80)] public string? Phone { get; set; }
    [MaxLength(160)] public string? Email { get; set; }
    public Guid? PrimaryOperatorId { get; set; }
    public Operator? PrimaryOperator { get; set; }
    public Guid? RoomReservationId { get; set; }
    public RoomReservation? RoomReservation { get; set; }
    [MaxLength(80)] public string? EstimatedHotelArrival { get; set; }
    [MaxLength(1000)] public string? DietaryRestrictions { get; set; }
    [MaxLength(2000)] public string? Notes { get; set; }
    [MaxLength(160)] public string? InternalOwner { get; set; }
    [MaxLength(500)] public string? NextAction { get; set; }
    public DateOnly? NextActionDueDate { get; set; }
    public Guid? CreatedById { get; set; }
    public Guid? UpdatedById { get; set; }
    public ICollection<PassengerFlight> PassengerFlights { get; set; } = [];
    public ICollection<BaggageEntitlement> BaggageEntitlements { get; set; } = [];
    public ICollection<PassengerTransfer> PassengerTransfers { get; set; } = [];
    public ICollection<FollowUp> FollowUps { get; set; } = [];
}

public sealed class RoomReservation : Entity
{
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;
    [MaxLength(60)] public required string InternalCode { get; set; }
    public Guid OperatorId { get; set; }
    public Operator Operator { get; set; } = null!;
    public VerificationStatus Status { get; set; } = VerificationStatus.ToVerify;
    [MaxLength(240)] public string? Hotel { get; set; }
    [MaxLength(160)] public string? RoomType { get; set; }
    public int ExpectedCapacity { get; set; }
    public bool CapacityOverride { get; set; }
    [MaxLength(500)] public string? CapacityOverrideReason { get; set; }
    public DateOnly? CheckIn { get; set; }
    public DateOnly? CheckOut { get; set; }
    [MaxLength(120)] public string? HotelReservationNumber { get; set; }
    [MaxLength(100)] public string? MealPlan { get; set; }
    [MaxLength(500)] public string? SourceReference { get; set; }
    [MaxLength(160)] public string? OperatorContact { get; set; }
    [MaxLength(2000)] public string? Notes { get; set; }
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
    [MaxLength(120)] public string? Airline { get; set; }
    [MaxLength(120)] public string? IssuingAgency { get; set; }
    [MaxLength(80)] public string? Pnr { get; set; }
    [MaxLength(200)] public string? GeneralReference { get; set; }
    [MaxLength(500)] public string? SourceReference { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public Guid? VerifiedById { get; set; }
    [MaxLength(2000)] public string? Notes { get; set; }
    public ICollection<FlightSegment> Segments { get; set; } = [];
    public ICollection<PassengerFlight> PassengerFlights { get; set; } = [];
}

public sealed class FlightSegment : Entity
{
    public Guid FlightBookingId { get; set; }
    public FlightBooking FlightBooking { get; set; } = null!;
    public SegmentType Type { get; set; }
    [MaxLength(30)] public string? FlightNumber { get; set; }
    [MaxLength(10)] public string? OriginAirport { get; set; }
    [MaxLength(10)] public string? DestinationAirport { get; set; }
    public DateTimeOffset? DepartureAt { get; set; }
    public DateTimeOffset? ArrivalAt { get; set; }
    [MaxLength(80)] public string? OriginTimeZone { get; set; }
    [MaxLength(80)] public string? DestinationTimeZone { get; set; }
    public int Sequence { get; set; }
}

public sealed class PassengerFlight
{
    public Guid PassengerId { get; set; }
    public Passenger Passenger { get; set; } = null!;
    public Guid FlightBookingId { get; set; }
    public FlightBooking FlightBooking { get; set; } = null!;
    [MaxLength(80)] public string? ElectronicTicketNumber { get; set; }
    public VerificationStatus TicketStatus { get; set; } = VerificationStatus.ToVerify;
    [MaxLength(1000)] public string? Notes { get; set; }
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
    public bool Includes23Kg { get; set; }
    public bool AppliesOutbound { get; set; }
    public bool AppliesReturn { get; set; }
    [MaxLength(500)] public string? ExceptionReason { get; set; }
    [MaxLength(500)] public string? SourceReference { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public Guid? VerifiedById { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
}

public sealed class TransferBooking : Entity
{
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;
    public VerificationStatus Status { get; set; } = VerificationStatus.ToVerify;
    [MaxLength(160)] public string? Company { get; set; }
    [MaxLength(120)] public string? VoucherCode { get; set; }
    [MaxLength(160)] public string? Contact { get; set; }
    [MaxLength(120)] public string? Airport { get; set; }
    [MaxLength(240)] public string? Hotel { get; set; }
    public TransferCoverage Coverage { get; set; }
    public DateTimeOffset? ArrivalPickupAt { get; set; }
    public DateTimeOffset? DeparturePickupAt { get; set; }
    [MaxLength(500)] public string? SourceReference { get; set; }
    [MaxLength(2000)] public string? Notes { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public Guid? VerifiedById { get; set; }
    public ICollection<PassengerTransfer> PassengerTransfers { get; set; } = [];
}

public sealed class PassengerTransfer
{
    public Guid PassengerId { get; set; }
    public Passenger Passenger { get; set; } = null!;
    public Guid TransferBookingId { get; set; }
    public TransferBooking TransferBooking { get; set; } = null!;
}

public sealed class Attachment : Entity
{
    public DocumentType DocumentType { get; set; }
    [MaxLength(255)] public required string OriginalName { get; set; }
    [MaxLength(255)] public required string StoredName { get; set; }
    [MaxLength(120)] public required string MimeType { get; set; }
    public long Size { get; set; }
    [MaxLength(500)] public required string SecurePath { get; set; }
    [MaxLength(64)] public required string Sha256 { get; set; }
    public Guid UploadedById { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(1000)] public string? Description { get; set; }
    public Guid? PassengerId { get; set; }
    public Guid? RoomReservationId { get; set; }
    public Guid? FlightBookingId { get; set; }
    public Guid? BaggageEntitlementId { get; set; }
    public Guid? TransferBookingId { get; set; }
}

public sealed class FollowUp : Entity
{
    public Guid? PassengerId { get; set; }
    public Passenger? Passenger { get; set; }
    public Guid? RoomReservationId { get; set; }
    public RoomReservation? RoomReservation { get; set; }
    [MaxLength(240)] public required string Title { get; set; }
    [MaxLength(2000)] public string? Description { get; set; }
    [MaxLength(160)] public string? Owner { get; set; }
    public DateOnly? DueDate { get; set; }
    public FollowUpStatus Status { get; set; }
    public FollowUpPriority Priority { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}

public sealed class AuditLog
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    [MaxLength(160)] public string? UserName { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(120)] public required string EntityName { get; set; }
    [MaxLength(80)] public required string EntityId { get; set; }
    [MaxLength(80)] public required string Action { get; set; }
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    [MaxLength(80)] public string? IpContext { get; set; }
}

public sealed class ImportRun : Entity
{
    [MaxLength(255)] public required string FileName { get; set; }
    [MaxLength(64)] public required string Sha256 { get; set; }
    public bool DryRun { get; set; }
    [MaxLength(40)] public required string Status { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Errors { get; set; }
    public string SummaryJson { get; set; } = "{}";
    public Guid? UserId { get; set; }
}
