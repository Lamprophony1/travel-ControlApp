using Microsoft.EntityFrameworkCore;
using TravelControl.Api.Contracts;
using TravelControl.Api.Data;
using TravelControl.Api.Domain;

namespace TravelControl.Api.Services;

public sealed class PassengerQueryService(AppDbContext db)
{
    public IQueryable<Passenger> BaseQuery() => db.Passengers.AsSplitQuery()
        .Include(x => x.Trip).Include(x => x.PrimaryOperator).Include(x => x.RoomReservation)!.ThenInclude(x => x!.Passengers)
        .Include(x => x.PassengerFlights).ThenInclude(x => x.FlightBooking).ThenInclude(x => x.Segments)
        .Include(x => x.BaggageEntitlements)
        .Include(x => x.PassengerTransfers).ThenInclude(x => x.TransferBooking).ThenInclude(x => x.PassengerTransfers)
        .Include(x => x.FollowUps);

    public static PassengerListItem Map(Passenger p, DateOnly today)
    {
        var state = BusinessRules.CalculatePassenger(p, today);
        return new PassengerListItem(p.Id, p.FullName, MaskPassport(p.PassportNumber), state.PassportStatus,
            p.PrimaryOperator?.Name, p.RoomReservation?.InternalCode, p.RoomReservation?.Hotel, p.RoomReservation?.RoomType,
            p.RoomReservation?.CheckIn, p.RoomReservation?.CheckOut, p.RoomReservation?.Nights,
            p.DocumentationStatus, state.OverallStatus, state.ProgressPercent, state.Requirements, state.Alerts,
            p.NextAction, p.InternalOwner, p.NextActionDueDate, p.UpdatedAt, p.Version);
    }

    public static string MaskPassport(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Sin cargar";
        var visible = value.Length <= 3 ? value[^1..] : value[^3..];
        return new string('•', Math.Max(4, value.Length - visible.Length)) + visible;
    }
}
