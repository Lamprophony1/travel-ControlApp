using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Text.Json;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;

namespace TravelControl.Infrastructure.Services;

public sealed record TicketAccessGenerationPreview(
    int TicketedPassengers,
    int CopaPassengers,
    int LatamPassengers,
    int CopaGenerable,
    int CopaVerifiable,
    int LatamWithOrderId,
    int LatamWithoutOrderId,
    int PendingLastNames,
    int NewLinks,
    int ExistingLinks,
    int InvalidLinks,
    int Errors);

public sealed class TicketAccessLinkService(AppDbContext db)
{
    public async Task<TicketAccessGenerationPreview> PreviewAsync(CancellationToken ct)
    {
        var links = await TicketedQuery().AsNoTracking().ToListAsync(ct);
        return BuildPreview(links);
    }

    public async Task<TicketAccessGenerationPreview> CommitAsync(Guid userId, string? userName, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var links = await TicketedQuery().ToListAsync(ct);
        var preview = BuildPreview(links);
        var now = DateTimeOffset.UtcNow;
        var changed = 0;

        foreach (var link in links)
        {
            var generated = BuildUrl(link.FlightBooking, link);
            if (generated is null || !IsSafeOfficialUrl(generated)) continue;
            if (string.IsNullOrWhiteSpace(link.BookingLookupLastName)
                && !string.IsNullOrWhiteSpace(link.Passenger?.LastNames))
                link.BookingLookupLastName = link.Passenger.LastNames.Trim();
            if (string.Equals(link.TicketAccessUrl, generated, StringComparison.Ordinal)
                && link.TicketAccessStatus == TicketAccessStatus.Verified) continue;
            if (!string.Equals(link.TicketAccessUrl, generated, StringComparison.Ordinal)
                || link.TicketAccessStatus is TicketAccessStatus.Missing or TicketAccessStatus.Invalid)
            {
                link.TicketAccessUrl = generated;
                link.TicketAccessStatus = TicketAccessStatus.Generated;
                link.TicketAccessGeneratedAt = now;
                link.TicketAccessVerifiedAt = null;
                link.UpdatedById = userId;
                changed++;
            }
        }

        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            UserName = userName,
            EntityName = "TicketAccess",
            EntityId = "generation",
            Action = "CommitGeneration",
            NewValue = JsonSerializer.Serialize(new
            {
                changed,
                preview.TicketedPassengers,
                preview.CopaPassengers,
                preview.LatamPassengers,
                preview.LatamWithoutOrderId,
                preview.PendingLastNames,
                preview.Errors
            })
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return preview with { NewLinks = changed };
    }

    public static string? BuildUrl(FlightBooking booking, PassengerFlight link)
    {
        var lookupLastName = EffectiveLookupLastName(link);
        if (IsCopa(booking.Airline))
        {
            if (string.IsNullOrWhiteSpace(booking.Pnr) || string.IsNullOrWhiteSpace(lookupLastName)) return null;
            var pnr = NormalizeIdentifier(booking.Pnr, upper: true);
            var lastName = NormalizeLookup(lookupLastName, upper: true);
            return pnr.Length == 0 || lastName.Length == 0
                ? null
                : $"https://mytrips.copaair.com/trip-detail/{Uri.EscapeDataString(pnr)}/{Uri.EscapeDataString(lastName)}";
        }

        if (IsLatam(booking.Airline))
        {
            if (string.IsNullOrWhiteSpace(link.AirlineOrderId) || string.IsNullOrWhiteSpace(lookupLastName)) return null;
            var orderId = link.AirlineOrderId.Trim();
            var lastName = NormalizeLookup(lookupLastName, upper: false);
            return orderId.Length == 0 || lastName.Length == 0
                ? null
                : $"https://www.latamairlines.com/py/es/mis-viajes/second-detail?orderId={Uri.EscapeDataString(orderId)}&lastname={Uri.EscapeDataString(lastName)}";
        }

        return null;
    }

    public static bool IsSafeOfficialUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            return false;
        if (string.Equals(uri.Host, "mytrips.copaair.com", StringComparison.OrdinalIgnoreCase))
            return uri.AbsolutePath.StartsWith("/trip-detail/", StringComparison.Ordinal) && uri.Query.Length == 0;
        if (string.Equals(uri.Host, "www.latamairlines.com", StringComparison.OrdinalIgnoreCase))
            return string.Equals(uri.AbsolutePath, "/py/es/mis-viajes/second-detail", StringComparison.Ordinal)
                && HasNonEmptyQueryValue(uri.Query, "orderId") && HasNonEmptyQueryValue(uri.Query, "lastname");
        return false;
    }

    public static bool IsSafeOfficialUrlForAirline(string? airline, string? value)
    {
        if (!IsSafeOfficialUrl(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        return IsCopa(airline)
            ? string.Equals(uri.Host, "mytrips.copaair.com", StringComparison.OrdinalIgnoreCase)
            : IsLatam(airline) && string.Equals(uri.Host, "www.latamairlines.com", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeLookup(string value, bool upper)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character)) builder.Append(character);
        }
        var normalized = builder.ToString().Normalize(NormalizationForm.FormC);
        return upper ? normalized.ToUpperInvariant() : normalized.ToLowerInvariant();
    }

    public static bool IsCopa(string? airline) => !string.IsNullOrWhiteSpace(airline)
        && (airline.Contains("Copa", StringComparison.OrdinalIgnoreCase)
            || string.Equals(airline.Trim(), "CM", StringComparison.OrdinalIgnoreCase));

    public static bool IsLatam(string? airline) => !string.IsNullOrWhiteSpace(airline)
        && (airline.Contains("LATAM", StringComparison.OrdinalIgnoreCase)
            || string.Equals(airline.Trim(), "LA", StringComparison.OrdinalIgnoreCase));

    private IQueryable<PassengerFlight> TicketedQuery() => db.PassengerFlights
        .Include(x => x.FlightBooking)
        .Include(x => x.Passenger)
        .Where(x => x.TicketStatus == VerificationStatus.Confirmed);

    private static TicketAccessGenerationPreview BuildPreview(IReadOnlyCollection<PassengerFlight> links)
    {
        var copa = links.Where(x => IsCopa(x.FlightBooking.Airline)).ToArray();
        var latam = links.Where(x => IsLatam(x.FlightBooking.Airline)).ToArray();
        var generable = links.Select(x => new { Link = x, Url = BuildUrl(x.FlightBooking, x) }).ToArray();
        var existing = links.Count(x => IsSafeOfficialUrl(x.TicketAccessUrl));
        var invalid = links.Count(x => x.TicketAccessStatus == TicketAccessStatus.Invalid
            || !string.IsNullOrWhiteSpace(x.TicketAccessUrl) && !IsSafeOfficialUrl(x.TicketAccessUrl));
        return new(
            links.Select(x => x.PassengerId).Distinct().Count(),
            copa.Length,
            latam.Length,
            copa.Count(x => BuildUrl(x.FlightBooking, x) is not null),
            copa.Count(x => x.TicketAccessStatus == TicketAccessStatus.Verified && IsSafeOfficialUrl(x.TicketAccessUrl)),
            latam.Count(x => !string.IsNullOrWhiteSpace(x.AirlineOrderId)),
            latam.Count(x => string.IsNullOrWhiteSpace(x.AirlineOrderId)),
            links.Count(x => string.IsNullOrWhiteSpace(EffectiveLookupLastName(x))),
            generable.Count(x => x.Url is not null && !string.Equals(x.Url, x.Link.TicketAccessUrl, StringComparison.Ordinal)),
            existing,
            invalid,
            links.Count(x => !IsCopa(x.FlightBooking.Airline) && !IsLatam(x.FlightBooking.Airline)));
    }

    private static string NormalizeIdentifier(string value, bool upper)
    {
        var normalized = new string(value.Trim().Where(char.IsLetterOrDigit).ToArray());
        return upper ? normalized.ToUpperInvariant() : normalized.ToLowerInvariant();
    }

    private static string? EffectiveLookupLastName(PassengerFlight link) =>
        !string.IsNullOrWhiteSpace(link.BookingLookupLastName)
            ? link.BookingLookupLastName
            : link.Passenger?.LastNames;

    private static bool HasNonEmptyQueryValue(string query, string name) => query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .Any(parts => parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), name, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(Uri.UnescapeDataString(parts[1])));
}
