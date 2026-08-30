using Microsoft.EntityFrameworkCore;
using TravelControl.Application.Services;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;

namespace TravelControl.Infrastructure.Services;

public sealed record RelatedEvidence(
    Guid AttachmentId,
    Guid? LinkId,
    DocumentType DocumentType,
    string OriginalName,
    string MimeType,
    long Size,
    DateTimeOffset UploadedAt,
    string Source,
    Guid SourceId);

public sealed class EvidenceResolver(AppDbContext db)
{
    public async Task<IReadOnlyDictionary<Guid, PassengerEvidenceState>> GetForPassengersAsync(
        IEnumerable<Guid> passengerIds,
        CancellationToken ct)
    {
        var ids = passengerIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, PassengerEvidenceState>();

        var associations = await db.Passengers.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new
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
            .Where(x => x.PassengerId.HasValue && ids.Contains(x.PassengerId.Value)
                && x.Attachment.DocumentType == DocumentType.AirTicket)
            .Select(x => x.PassengerId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
        directTickets.UnionWith(await db.Attachments.AsNoTracking()
            .Where(x => x.PassengerId.HasValue && ids.Contains(x.PassengerId.Value)
                && x.DocumentType == DocumentType.AirTicket)
            .Select(x => x.PassengerId!.Value).Distinct().ToListAsync(ct));

        var ticketFlights = (await db.AttachmentLinks.AsNoTracking()
            .Where(x => x.FlightBookingId.HasValue && flightIds.Contains(x.FlightBookingId.Value)
                && x.Attachment.DocumentType == DocumentType.AirTicket)
            .Select(x => x.FlightBookingId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
        ticketFlights.UnionWith(await db.Attachments.AsNoTracking()
            .Where(x => x.FlightBookingId.HasValue && flightIds.Contains(x.FlightBookingId.Value)
                && x.DocumentType == DocumentType.AirTicket)
            .Select(x => x.FlightBookingId!.Value).Distinct().ToListAsync(ct));
        ticketFlights.UnionWith(await db.FlightBookings.AsNoTracking()
            .Where(x => flightIds.Contains(x.Id) && x.SourceReference != null && x.SourceReference != "")
            .Select(x => x.Id).ToListAsync(ct));

        var voucherRooms = await GetRoomEvidenceAsync(roomIds, ct);
        var baggageProofs = (await db.AttachmentLinks.AsNoTracking()
            .Where(x => x.BaggageEntitlementId.HasValue && baggageIds.Contains(x.BaggageEntitlementId.Value)
                && x.Attachment.DocumentType == DocumentType.BaggageProof)
            .Select(x => x.BaggageEntitlementId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
        baggageProofs.UnionWith(await db.Attachments.AsNoTracking()
            .Where(x => x.BaggageEntitlementId.HasValue && baggageIds.Contains(x.BaggageEntitlementId.Value)
                && x.DocumentType == DocumentType.BaggageProof)
            .Select(x => x.BaggageEntitlementId!.Value).Distinct().ToListAsync(ct));
        var baggageFlights = (await db.AttachmentLinks.AsNoTracking()
            .Where(x => x.FlightBookingId.HasValue && flightIds.Contains(x.FlightBookingId.Value)
                && x.Attachment.DocumentType == DocumentType.BaggageProof)
            .Select(x => x.FlightBookingId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
        baggageFlights.UnionWith(await db.Attachments.AsNoTracking()
            .Where(x => x.FlightBookingId.HasValue && flightIds.Contains(x.FlightBookingId.Value)
                && x.DocumentType == DocumentType.BaggageProof)
            .Select(x => x.FlightBookingId!.Value).Distinct().ToListAsync(ct));

        return associations.ToDictionary(
            x => x.Id,
            x => new PassengerEvidenceState(
                directTickets.Contains(x.Id) || x.FlightIds.Any(ticketFlights.Contains),
                x.RoomReservationId.HasValue && voucherRooms.Contains(x.RoomReservationId.Value),
                x.BaggageIds.Any(baggageProofs.Contains) || x.FlightIds.Any(baggageFlights.Contains)));
    }

    public async Task<HashSet<Guid>> GetRoomEvidenceAsync(IEnumerable<Guid> roomIds, CancellationToken ct)
    {
        var ids = roomIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var result = (await db.AttachmentLinks.AsNoTracking()
            .Where(x => x.RoomReservationId.HasValue && ids.Contains(x.RoomReservationId.Value)
                && x.Attachment.DocumentType == DocumentType.HotelVoucher)
            .Select(x => x.RoomReservationId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
        result.UnionWith(await db.Attachments.AsNoTracking()
            .Where(x => x.RoomReservationId.HasValue && ids.Contains(x.RoomReservationId.Value)
                && x.DocumentType == DocumentType.HotelVoucher)
            .Select(x => x.RoomReservationId!.Value).Distinct().ToListAsync(ct));
        return result;
    }

    public async Task<IReadOnlyList<RelatedEvidence>> GetPassengerEvidenceAsync(Guid passengerId, CancellationToken ct)
    {
        var passenger = await db.Passengers.AsNoTracking().Where(x => x.Id == passengerId)
            .Select(x => new
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
                x.Id,
                x.AttachmentId,
                x.Attachment.DocumentType,
                x.Attachment.OriginalName,
                x.Attachment.MimeType,
                x.Attachment.Size,
                x.Attachment.UploadedAt,
                x.PassengerId,
                x.RoomReservationId,
                x.FlightBookingId,
                x.BaggageEntitlementId
            }).ToListAsync(ct);
        return links.Select(x => new RelatedEvidence(
                x.AttachmentId,
                x.Id,
                x.DocumentType,
                x.OriginalName,
                x.MimeType,
                x.Size,
                x.UploadedAt,
                x.PassengerId.HasValue ? "Direct" : x.FlightBookingId.HasValue ? "FlightBooking"
                    : x.RoomReservationId.HasValue ? "RoomReservation" : "BaggageEntitlement",
                x.PassengerId ?? x.FlightBookingId ?? x.RoomReservationId ?? x.BaggageEntitlementId!.Value))
            .OrderByDescending(x => x.UploadedAt)
            .ThenBy(x => x.OriginalName)
            .ToArray();
    }

    public async Task<DateTimeOffset> GetOperationalUpdatedAtAsync(DateTimeOffset fallback, CancellationToken ct)
    {
        var values = new List<DateTimeOffset> { fallback };
        values.AddRange(await db.Trips.AsNoTracking().Where(x => x.IsActive).Select(x => x.UpdatedAt).ToListAsync(ct));
        values.AddRange(await db.Passengers.AsNoTracking().Select(x => x.UpdatedAt).ToListAsync(ct));
        values.AddRange(await db.RoomReservations.AsNoTracking().Select(x => x.UpdatedAt).ToListAsync(ct));
        values.AddRange(await db.FlightBookings.AsNoTracking().Select(x => x.UpdatedAt).ToListAsync(ct));
        values.AddRange(await db.FlightSegments.AsNoTracking().Select(x => x.UpdatedAt).ToListAsync(ct));
        values.AddRange(await db.BaggageEntitlements.AsNoTracking().Select(x => x.UpdatedAt).ToListAsync(ct));
        values.AddRange(await db.TripTransferStatuses.AsNoTracking().Select(x => x.UpdatedAt).ToListAsync(ct));
        values.AddRange(await db.Attachments.AsNoTracking().Select(x => x.UploadedAt).ToListAsync(ct));
        values.AddRange(await db.Attachments.AsNoTracking().Select(x => x.UpdatedAt).ToListAsync(ct));
        values.AddRange(await db.AttachmentLinks.AsNoTracking().Select(x => x.CreatedAt).ToListAsync(ct));
        values.AddRange(await db.FollowUps.AsNoTracking().Select(x => x.UpdatedAt).ToListAsync(ct));
        var operationalEntities = new[] { "Passenger", "RoomReservation", "FlightBooking", "PassengerFlight", "BaggageEntitlement", "TripTransferStatus", "Attachment", "AttachmentLink", "FollowUp" };
        values.AddRange(await db.AuditLogs.AsNoTracking().Where(x => operationalEntities.Contains(x.EntityName)).Select(x => x.At).ToListAsync(ct));
        return values.Max();
    }
}
