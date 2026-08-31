using Microsoft.EntityFrameworkCore;
using TravelControl.Application.Services;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;

namespace TravelControl.Infrastructure.Services;

public sealed record RelatedEvidence(
    Guid AttachmentId, Guid LinkId, DocumentType EvidenceType, string SourceType, Guid SourceId,
    string SourceLabel, bool IsDirect, bool CanUnlinkHere, int AffectedPassengerCount, string? ManagePath,
    string OriginalName, string MimeType, long Size, DateTimeOffset UploadedAt);

public sealed class EvidenceResolver(AppDbContext db)
{
    public async Task<IReadOnlyDictionary<Guid, PassengerEvidenceState>> GetForPassengersAsync(
        IEnumerable<Guid> passengerIds, CancellationToken ct)
    {
        var ids = passengerIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, PassengerEvidenceState>();

        var associations = await db.Passengers.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new
        {
            x.Id,
            x.RoomReservationId,
            FlightIds = x.PassengerFlights.Select(f => f.FlightBookingId).ToArray(),
            BaggageIds = x.BaggageEntitlements.Select(b => b.Id).ToArray()
        }).ToListAsync(ct);
        var flightIds = associations.SelectMany(x => x.FlightIds).Distinct().ToArray();
        var roomIds = associations.Where(x => x.RoomReservationId.HasValue).Select(x => x.RoomReservationId!.Value).Distinct().ToArray();
        var baggageIds = associations.SelectMany(x => x.BaggageIds).Distinct().ToArray();

        var directTickets = (await db.AttachmentLinks.AsNoTracking()
            .Where(x => x.PassengerId.HasValue && ids.Contains(x.PassengerId.Value) && x.EvidenceType == DocumentType.AirTicket)
            .Select(x => x.PassengerId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
        var ticketFlights = (await db.AttachmentLinks.AsNoTracking()
            .Where(x => x.FlightBookingId.HasValue && flightIds.Contains(x.FlightBookingId.Value) && x.EvidenceType == DocumentType.AirTicket)
            .Select(x => x.FlightBookingId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
        var voucherRooms = await GetRoomEvidenceAsync(roomIds, ct);
        var baggageProofs = (await db.AttachmentLinks.AsNoTracking()
            .Where(x => x.BaggageEntitlementId.HasValue && baggageIds.Contains(x.BaggageEntitlementId.Value) && x.EvidenceType == DocumentType.BaggageProof)
            .Select(x => x.BaggageEntitlementId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
        var baggageFlights = (await db.AttachmentLinks.AsNoTracking()
            .Where(x => x.FlightBookingId.HasValue && flightIds.Contains(x.FlightBookingId.Value) && x.EvidenceType == DocumentType.BaggageProof)
            .Select(x => x.FlightBookingId!.Value).Distinct().ToListAsync(ct)).ToHashSet();

        return associations.ToDictionary(x => x.Id, x => new PassengerEvidenceState(
            directTickets.Contains(x.Id) || x.FlightIds.Any(ticketFlights.Contains),
            x.RoomReservationId.HasValue && voucherRooms.Contains(x.RoomReservationId.Value),
            x.BaggageIds.Any(baggageProofs.Contains) || x.FlightIds.Any(baggageFlights.Contains)));
    }

    public async Task<HashSet<Guid>> GetRoomEvidenceAsync(IEnumerable<Guid> roomIds, CancellationToken ct)
    {
        var ids = roomIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return (await db.AttachmentLinks.AsNoTracking()
            .Where(x => x.RoomReservationId.HasValue && ids.Contains(x.RoomReservationId.Value) && x.EvidenceType == DocumentType.HotelVoucher)
            .Select(x => x.RoomReservationId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
    }

    public async Task<IReadOnlyList<RelatedEvidence>> GetPassengerEvidenceAsync(Guid passengerId, CancellationToken ct)
    {
        var passenger = await db.Passengers.AsNoTracking().Where(x => x.Id == passengerId).Select(x => new
        {
            x.RoomReservationId,
            FlightIds = x.PassengerFlights.Select(f => f.FlightBookingId).ToArray(),
            BaggageIds = x.BaggageEntitlements.Select(b => b.Id).ToArray()
        }).SingleOrDefaultAsync(ct);
        if (passenger is null) return [];

        var links = await db.AttachmentLinks.AsNoTracking()
            .Where(x => x.PassengerId == passengerId
                || (x.RoomReservationId.HasValue && x.RoomReservationId == passenger.RoomReservationId)
                || (x.FlightBookingId.HasValue && passenger.FlightIds.Contains(x.FlightBookingId.Value))
                || (x.BaggageEntitlementId.HasValue && passenger.BaggageIds.Contains(x.BaggageEntitlementId.Value)))
            .Select(x => new
            {
                x.Id, x.AttachmentId, x.EvidenceType, x.Attachment.OriginalName, x.Attachment.MimeType,
                x.Attachment.Size, x.Attachment.UploadedAt, x.PassengerId, x.RoomReservationId,
                RoomCode = x.RoomReservation == null ? null : x.RoomReservation.InternalCode,
                RoomPassengers = x.RoomReservation == null ? 0 : x.RoomReservation.Passengers.Count,
                x.FlightBookingId, Pnr = x.FlightBooking == null ? null : x.FlightBooking.Pnr,
                FlightPassengers = x.FlightBooking == null ? 0 : x.FlightBooking.PassengerFlights.Count,
                x.BaggageEntitlementId
            }).ToListAsync(ct);
        return links.Select(x =>
            {
                if (x.PassengerId.HasValue)
                    return new RelatedEvidence(x.AttachmentId, x.Id, x.EvidenceType, "Passenger", x.PassengerId.Value,
                        "Vinculado directamente", true, true, 1, null, x.OriginalName, x.MimeType, x.Size, x.UploadedAt);
                if (x.FlightBookingId.HasValue)
                    return new RelatedEvidence(x.AttachmentId, x.Id, x.EvidenceType, "FlightBooking", x.FlightBookingId.Value,
                        $"PNR {x.Pnr ?? "sin código"}", false, false, x.FlightPassengers, "/gestion/vuelos",
                        x.OriginalName, x.MimeType, x.Size, x.UploadedAt);
                if (x.RoomReservationId.HasValue)
                    return new RelatedEvidence(x.AttachmentId, x.Id, x.EvidenceType, "RoomReservation", x.RoomReservationId.Value,
                        $"Habitación {x.RoomCode ?? "sin código"}", false, false, x.RoomPassengers, "/gestion/habitaciones",
                        x.OriginalName, x.MimeType, x.Size, x.UploadedAt);
                return new RelatedEvidence(x.AttachmentId, x.Id, x.EvidenceType, "BaggageEntitlement", x.BaggageEntitlementId!.Value,
                    "Equipaje individual (legacy)", false, false, 1, "/gestion/vuelos?focus=baggage", x.OriginalName, x.MimeType, x.Size, x.UploadedAt);
            })
            .OrderByDescending(x => x.UploadedAt).ThenBy(x => x.OriginalName).ToArray();
    }

    public async Task<DateTimeOffset> GetOperationalUpdatedAtAsync(DateTimeOffset fallback, CancellationToken ct)
    {
        var values = new List<DateTimeOffset> { fallback };
        values.AddRange(await db.Trips.AsNoTracking().Where(x => x.IsActive).Select(x => x.UpdatedAt).ToListAsync(ct));
        values.AddRange(await db.Passengers.AsNoTracking().Select(x => x.UpdatedAt).ToListAsync(ct));
        values.AddRange(await db.RoomReservations.AsNoTracking().Select(x => x.UpdatedAt).ToListAsync(ct));
        values.AddRange(await db.FlightBookings.AsNoTracking().Select(x => x.UpdatedAt).ToListAsync(ct));
        values.AddRange(await db.FlightSegments.AsNoTracking().Select(x => x.UpdatedAt).ToListAsync(ct));
        values.AddRange(await db.PassengerFlights.AsNoTracking().Select(x => x.UpdatedAt).ToListAsync(ct));
        values.AddRange(await db.BaggageEntitlements.AsNoTracking().Select(x => x.UpdatedAt).ToListAsync(ct));
        values.AddRange(await db.TripTransferStatuses.AsNoTracking().Select(x => x.UpdatedAt).ToListAsync(ct));
        values.AddRange(await db.Attachments.AsNoTracking().Select(x => x.UploadedAt).ToListAsync(ct));
        values.AddRange(await db.Attachments.AsNoTracking().Select(x => x.UpdatedAt).ToListAsync(ct));
        values.AddRange(await db.AttachmentLinks.AsNoTracking().Select(x => x.UpdatedAt).ToListAsync(ct));
        values.AddRange(await db.FollowUps.AsNoTracking().Select(x => x.UpdatedAt).ToListAsync(ct));
        var operationalEntities = new[] { "Passenger", "RoomReservation", "FlightBooking", "PassengerFlight", "BaggageEntitlement", "TripTransferStatus", "Attachment", "AttachmentLink", "FollowUp" };
        values.AddRange(await db.AuditLogs.AsNoTracking().Where(x => operationalEntities.Contains(x.EntityName)).Select(x => x.At).ToListAsync(ct));
        return values.Max();
    }
}
