using TravelControl.Domain;

namespace TravelControl.Application.Contracts;

public sealed record PublicRequirementDto(string Key, string Label, VerificationStatus Status);
public sealed record PublicFlightDto(string Airline, VerificationStatus TicketStatus, bool HasTicketAccess, string? TicketAccessPath);
public sealed record PublicPassengerDto(
    Guid Id,
    string Name,
    string? Operator,
    string? RoomCode,
    string? Hotel,
    string? RoomType,
    DateOnly? CheckIn,
    DateOnly? CheckOut,
    PassengerOverallStatus OverallStatus,
    int ProgressPercent,
    IReadOnlyList<PublicRequirementDto> Requirements,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Alerts,
    bool TransferConfirmed,
    IReadOnlyList<PublicFlightDto> Flights);

public sealed record PublicDashboardKpi(string Key, string Label, int Value, int Total, int Percent);
public sealed record PublicCategoryProgress(
    string Key,
    string Label,
    int Confirmed,
    int Pending,
    int InProgress,
    int NotIncluded,
    int NotApplicable,
    int ResolvedPercent);
public sealed record PublicOperatorSummary(string Name, int Rooms, int Passengers, int ResolvedRooms);
public sealed record PublicMissingCounts(
    int Tickets,
    int Baggage,
    int Documentation,
    int Passports,
    int PassengersWithoutResolvedAccommodation,
    int UnresolvedRoomReservations,
    int SpecificPropertiesPending,
    bool Transfer);
public sealed record PublicDashboardDto(
    string TripName,
    string Destination,
    int TotalPassengers,
    int ReadyPassengers,
    int PendingPassengers,
    int AttentionPassengers,
    int ProgressPercent,
    TripOverallStatus OverallStatus,
    bool TransferConfirmed,
    IReadOnlyList<PublicDashboardKpi> Kpis,
    IReadOnlyList<PublicCategoryProgress> Categories,
    IReadOnlyList<PublicOperatorSummary> Operators,
    IReadOnlyList<AirlineSummary> Airlines,
    PublicMissingCounts Missing,
    IReadOnlyList<string> Alerts,
    DateTimeOffset UpdatedAt);
