using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TravelControl.Application.Services;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;

namespace TravelControl.Infrastructure.Services;

public sealed record PassengerTravelImportIssue(
    string Level,
    int? Row,
    string? Field,
    string Message,
    string? MaskedExample = null);

public sealed record PassengerTravelMatchSuggestion(
    int Row,
    Guid PassengerId,
    string MaskedCandidate);

public sealed record PassengerTravelImportResult(
    int RowsRead,
    int MatchedPassengers,
    int UnmatchedPassengers,
    int AmbiguousMatches,
    int CreatedPassengers,
    int DeletedPassengers,
    int IdentityFieldsToComplete,
    int IdentityFieldsToUpdate,
    int PassportConflicts,
    int PnrsToCreate,
    int PnrsToUpdate,
    int AssociationsToCreate,
    int ExistingAssociations,
    int ConflictingAssociations,
    int TicketsToConfirm,
    int ConfirmedTicketPassengers,
    int PassengersWithoutTicket,
    IReadOnlyDictionary<string, int> AirlinesDetected,
    int IgnoredSecondaryDates,
    int BlockingErrors,
    int Warnings,
    string Sha256,
    bool CanCommit,
    IReadOnlyList<PassengerTravelImportIssue> Issues,
    IReadOnlyList<PassengerTravelMatchSuggestion> SuggestedMatches,
    int SourcePassengersWithPnr,
    int SourcePassengersWithoutPnr,
    int UniquePnrs,
    int PassportsProvided,
    int BirthDatesProvided,
    int PassportExpiriesProvided,
    int NationalitiesProvided,
    Guid? ImportRunId = null,
    int PnrsCreated = 0,
    int PnrsUpdated = 0,
    int AssociationsCreated = 0,
    int PassportsUpdated = 0,
    int BirthDatesUpdated = 0,
    int PassportExpiriesUpdated = 0,
    int NationalitiesUpdated = 0);

internal sealed record PassengerTravelRow(
    int Row,
    string Name,
    string NormalizedName,
    string? Passport,
    string? NormalizedPassport,
    DateOnly? BirthDate,
    DateOnly? PassportExpiry,
    string? Nationality,
    string? Pnr,
    string? Airline,
    string? AirlineCode,
    DateOnly? CheckIn,
    DateOnly? CheckOut);

public sealed class PassengerTravelManifestImportService(
    AppDbContext db,
    ILogger<PassengerTravelManifestImportService> logger)
{
    public const string SourceReference = "Listado privado actualizado de reservas — 30/08/2026";

    private static readonly string[] RequiredHeaders =
    [
        "row", "name", "passport", "birth_date", "passport_expiry", "nationality_code",
        "pnr", "airline_code", "check_in", "check_out"
    ];

    public async Task<PassengerTravelImportResult> PreviewAsync(
        Stream stream,
        string fileName,
        bool overwriteExistingIdentity,
        bool replaceConflictingFlightAssignments,
        IReadOnlyDictionary<int, Guid>? aliases,
        CancellationToken ct)
    {
        var parsed = await ParseAsync(stream, fileName, overwriteExistingIdentity,
            replaceConflictingFlightAssignments, aliases, ct);
        return parsed.Result;
    }

    public async Task<PassengerTravelImportResult> CommitAsync(
        Stream stream,
        string fileName,
        string previewHash,
        bool overwriteExistingIdentity,
        bool replaceConflictingFlightAssignments,
        bool confirmAuthoritativeUpdate,
        IReadOnlyDictionary<int, Guid>? aliases,
        Guid userId,
        string? userName,
        CancellationToken ct)
    {
        if (!confirmAuthoritativeUpdate)
            throw new InvalidOperationException("Confirmá expresamente la actualización autoritativa.");

        var parsed = await ParseAsync(stream, fileName, overwriteExistingIdentity,
            replaceConflictingFlightAssignments, aliases, ct);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(parsed.Hash.ToUpperInvariant()),
                Encoding.ASCII.GetBytes((previewHash ?? string.Empty).Trim().ToUpperInvariant())))
            throw new InvalidOperationException("El archivo cambió desde la vista previa. Volvé a revisarlo.");
        if (!parsed.Result.CanCommit) return parsed.Result;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var passportsUpdated = 0;
        var birthsUpdated = 0;
        var expiriesUpdated = 0;
        var nationalitiesUpdated = 0;
        var pnrsCreated = 0;
        var pnrsUpdated = 0;
        var associationsCreated = 0;

        foreach (var row in parsed.Rows)
        {
            if (!parsed.Matches.TryGetValue(row.Row, out var passenger)) continue;
            var changedFields = new List<string>();
            if (ApplyString(passenger.PassportNumber, row.Passport, overwriteExistingIdentity, value =>
                {
                    passenger.PassportNumber = value;
                    passenger.NormalizedPassportNumber = string.IsNullOrWhiteSpace(value) ? null : TextNormalizer.Normalize(value);
                }))
            {
                passportsUpdated++;
                changedFields.Add(nameof(Passenger.PassportNumber));
            }
            if (ApplyValue(passenger.BirthDate, row.BirthDate, overwriteExistingIdentity, value => passenger.BirthDate = value))
            {
                birthsUpdated++;
                changedFields.Add(nameof(Passenger.BirthDate));
            }
            if (ApplyValue(passenger.PassportExpiry, row.PassportExpiry, overwriteExistingIdentity, value => passenger.PassportExpiry = value))
            {
                expiriesUpdated++;
                changedFields.Add(nameof(Passenger.PassportExpiry));
            }
            if (ApplyString(passenger.Nationality, row.Nationality, overwriteExistingIdentity, value => passenger.Nationality = value))
            {
                nationalitiesUpdated++;
                changedFields.Add(nameof(Passenger.Nationality));
            }
            if (changedFields.Count > 0)
            {
                passenger.UpdatedById = userId;
                db.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    UserName = userName,
                    EntityName = "Passenger",
                    EntityId = passenger.Id.ToString(),
                    PassengerId = passenger.Id,
                    Action = "PassengerTravelIdentityUpdate",
                    NewValue = JsonSerializer.Serialize(new { changedFields })
                });
            }
        }

        var bookingsByPnr = parsed.Bookings
            .Where(x => !string.IsNullOrWhiteSpace(x.Pnr))
            .GroupBy(x => NormalizePnr(x.Pnr!)!)
            .ToDictionary(x => x.Key, x => x.First());

        foreach (var group in parsed.Rows.Where(x => x.Pnr is not null).GroupBy(x => x.Pnr!))
        {
            var first = group.First();
            if (!bookingsByPnr.TryGetValue(group.Key, out var booking))
            {
                booking = new FlightBooking
                {
                    TripId = parsed.TripId,
                    Pnr = first.Pnr,
                    Airline = first.Airline,
                    SourceReference = SourceReference,
                    Status = VerificationStatus.InProgress
                };
                db.FlightBookings.Add(booking);
                bookingsByPnr[group.Key] = booking;
                pnrsCreated++;
            }
            else
            {
                var changed = false;
                if (!string.Equals(booking.Airline, first.Airline, StringComparison.Ordinal))
                {
                    booking.Airline = first.Airline;
                    changed = true;
                }
                var reference = AppendReference(booking.SourceReference, SourceReference);
                if (!string.Equals(reference, booking.SourceReference, StringComparison.Ordinal))
                {
                    booking.SourceReference = reference;
                    changed = true;
                }
                if (changed) pnrsUpdated++;
            }

            foreach (var row in group)
            {
                var passenger = parsed.Matches[row.Row];
                var link = passenger.PassengerFlights.FirstOrDefault(x => NormalizePnr(x.FlightBooking.Pnr) == group.Key);
                if (link is null)
                {
                    var conflicting = passenger.PassengerFlights
                        .Where(x => NormalizePnr(x.FlightBooking.Pnr) != group.Key)
                        .ToArray();
                    if (conflicting.Length > 0 && !replaceConflictingFlightAssignments)
                        throw new InvalidOperationException("La asignación aérea cambió desde la vista previa.");
                    link = new PassengerFlight
                    {
                        Passenger = passenger,
                        PassengerId = passenger.Id,
                        FlightBooking = booking,
                        FlightBookingId = booking.Id,
                        TicketStatus = VerificationStatus.Confirmed,
                        UpdatedById = userId
                    };
                    passenger.PassengerFlights.Add(link);
                    booking.PassengerFlights.Add(link);
                    associationsCreated++;
                }
                else
                {
                    link.TicketStatus = VerificationStatus.Confirmed;
                    link.UpdatedById = userId;
                }
                db.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    UserName = userName,
                    EntityName = "PassengerFlight",
                    EntityId = $"{passenger.Id}:{booking.Id}",
                    PassengerId = passenger.Id,
                    Action = "PassengerTravelTicketConfirmation",
                    NewValue = JsonSerializer.Serialize(new { airline = first.AirlineCode, status = VerificationStatus.Confirmed })
                });
            }
            booking.Status = BusinessRules.DeriveFlightBookingStatus(booking);
            if (booking.Status == VerificationStatus.Confirmed)
            {
                booking.VerifiedAt = DateTimeOffset.UtcNow;
                booking.VerifiedById = userId;
            }
        }

        await db.SaveChangesAsync(ct);
        var run = new ImportRun
        {
            FileName = SafeFileName(fileName),
            Sha256 = parsed.Hash,
            DryRun = false,
            ImportType = "PassengerTravel",
            Status = "Completado",
            Matched = parsed.Result.MatchedPassengers,
            Conflicts = parsed.Result.ConflictingAssociations + parsed.Result.PassportConflicts,
            Added = pnrsCreated + associationsCreated,
            Updated = passportsUpdated + birthsUpdated + expiriesUpdated + nationalitiesUpdated + pnrsUpdated,
            Unchanged = parsed.Result.RowsRead - parsed.Result.TicketsToConfirm,
            Errors = 0,
            SummaryJson = JsonSerializer.Serialize(new
            {
                parsed.Result.RowsRead,
                parsed.Result.MatchedPassengers,
                parsed.Result.SourcePassengersWithPnr,
                parsed.Result.SourcePassengersWithoutPnr,
                parsed.Result.UniquePnrs,
                pnrsCreated,
                pnrsUpdated,
                associationsCreated,
                passportsUpdated,
                birthsUpdated,
                expiriesUpdated,
                nationalitiesUpdated
            }),
            UserId = userId
        };
        db.ImportRuns.Add(run);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            UserName = userName,
            EntityName = "ImportRun",
            EntityId = run.Id.ToString(),
            Action = "PassengerTravelCommit",
            NewValue = run.SummaryJson
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Passenger travel import completed. rows={Rows}; matched={Matched}; bookingsCreated={BookingsCreated}; associationsCreated={AssociationsCreated}; ticketsConfirmed={TicketsConfirmed}; pending={Pending}",
            parsed.Result.RowsRead,
            parsed.Result.MatchedPassengers,
            pnrsCreated,
            associationsCreated,
            parsed.Result.ConfirmedTicketPassengers,
            parsed.Result.PassengersWithoutTicket);

        return parsed.Result with
        {
            ImportRunId = run.Id,
            PnrsCreated = pnrsCreated,
            PnrsUpdated = pnrsUpdated,
            AssociationsCreated = associationsCreated,
            PassportsUpdated = passportsUpdated,
            BirthDatesUpdated = birthsUpdated,
            PassportExpiriesUpdated = expiriesUpdated,
            NationalitiesUpdated = nationalitiesUpdated
        };
    }

    private async Task<ParsedPassengerTravel> ParseAsync(
        Stream stream,
        string fileName,
        bool overwriteExistingIdentity,
        bool replaceConflictingFlightAssignments,
        IReadOnlyDictionary<int, Guid>? aliases,
        CancellationToken ct)
    {
        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        if (bytes.Length == 0) throw new InvalidOperationException("El archivo está vacío.");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var issues = new List<PassengerTravelImportIssue>();
        var rows = ParseRows(bytes, fileName, issues);

        var trip = await db.Trips.SingleAsync(x => x.IsActive, ct);
        var passengers = await db.Passengers
            .Where(x => x.TripId == trip.Id)
            .Include(x => x.PassengerFlights).ThenInclude(x => x.FlightBooking).ThenInclude(x => x.Segments)
            .Include(x => x.BaggageEntitlements)
            .ToListAsync(ct);
        var bookings = await db.FlightBookings.Where(x => x.TripId == trip.Id)
            .Include(x => x.PassengerFlights).Include(x => x.Segments).ToListAsync(ct);

        ValidateRows(rows, passengers, issues);
        var matches = MatchPassengers(rows, passengers, aliases, issues, out var suggestions, out var ambiguous);
        foreach (var row in rows.Where(x => x.NormalizedPassport is not null))
        {
            var owner = passengers.FirstOrDefault(x => x.NormalizedPassportNumber == row.NormalizedPassport);
            if (owner is not null && matches.TryGetValue(row.Row, out var matched) && owner.Id != matched.Id)
                issues.Add(new("Error", row.Row, "passport", "El pasaporte ya pertenece a otro pasajero.", MaskPassport(row.Passport)));
        }
        var duplicateMatches = matches.GroupBy(x => x.Value.Id).Where(x => x.Count() > 1).ToArray();
        foreach (var duplicate in duplicateMatches)
            foreach (var match in duplicate)
                issues.Add(new("Error", match.Key, "name", "Más de una fila intenta actualizar al mismo pasajero."));

        var identityComplete = 0;
        var identityUpdate = 0;
        var pnrUpdate = 0;
        var associationCreate = 0;
        var associationExisting = 0;
        var associationConflict = 0;
        var ticketsToConfirm = 0;
        var bookingsByPnr = bookings.Where(x => !string.IsNullOrWhiteSpace(x.Pnr))
            .GroupBy(x => NormalizePnr(x.Pnr!)!).ToDictionary(x => x.Key, x => x.First());

        foreach (var row in rows)
        {
            if (!matches.TryGetValue(row.Row, out var passenger)) continue;
            CompareIdentity(passenger.PassportNumber, row.Passport, ref identityComplete, ref identityUpdate);
            CompareIdentity(passenger.BirthDate, row.BirthDate, ref identityComplete, ref identityUpdate);
            CompareIdentity(passenger.PassportExpiry, row.PassportExpiry, ref identityComplete, ref identityUpdate);
            CompareIdentity(passenger.Nationality, row.Nationality, ref identityComplete, ref identityUpdate);
            if (row.Pnr is null) continue;

            if (!bookingsByPnr.TryGetValue(row.Pnr, out var booking))
            {
            }
            else if (!string.Equals(booking.Airline, row.Airline, StringComparison.Ordinal)
                     || !ContainsReference(booking.SourceReference, SourceReference))
            {
                pnrUpdate++;
            }

            var same = passenger.PassengerFlights.FirstOrDefault(x => NormalizePnr(x.FlightBooking.Pnr) == row.Pnr);
            if (same is not null)
            {
                associationExisting++;
                if (same.TicketStatus != VerificationStatus.Confirmed) ticketsToConfirm++;
                continue;
            }

            associationCreate++;
            ticketsToConfirm++;
            if (passenger.PassengerFlights.Count > 0)
            {
                associationConflict++;
                if (!replaceConflictingFlightAssignments)
                    issues.Add(new("Advertencia", row.Row, "pnr", "Existe otra asignación aérea. Confirmá expresamente agregar la nueva reserva sin eliminar la existente.", MaskCode(row.Pnr)));
            }
        }

        if (identityUpdate > 0 && !overwriteExistingIdentity)
            issues.Add(new("Advertencia", null, "identity", "Hay valores personales no vacíos que requieren confirmación explícita para sobrescribirse."));
        if (rows.Any(x => x.CheckIn.HasValue || x.CheckOut.HasValue))
            issues.Add(new("Advertencia", null, "check_in/check_out", "Las fechas secundarias fueron reconocidas y no se persistirán ni modificarán habitaciones o segmentos."));

        var blocking = issues.Count(x => x.Level == "Error");
        var warnings = issues.Count(x => x.Level == "Advertencia");
        var canCommit = blocking == 0 && matches.Count == rows.Count && duplicateMatches.Length == 0
            && (identityUpdate == 0 || overwriteExistingIdentity)
            && (associationConflict == 0 || replaceConflictingFlightAssignments);
        var sourceWithPnr = rows.Count(x => x.Pnr is not null);
        var airlines = new Dictionary<string, int>
        {
            ["Copa Airlines"] = rows.Count(x => x.AirlineCode == "CM"),
            ["LATAM Airlines"] = rows.Count(x => x.AirlineCode == "LA"),
            ["Sin aerolínea confirmada"] = rows.Count(x => x.Airline is null)
        };
        var result = new PassengerTravelImportResult(
            rows.Count,
            matches.Count,
            rows.Count - matches.Count - ambiguous,
            ambiguous,
            0,
            0,
            identityComplete,
            identityUpdate,
            issues.Count(x => x.Field == "passport" && x.Level == "Error"),
            rows.Where(x => x.Pnr is not null).Select(x => x.Pnr).Distinct().Count(pnr => !bookingsByPnr.ContainsKey(pnr!)),
            pnrUpdate,
            associationCreate,
            associationExisting,
            associationConflict,
            ticketsToConfirm,
            sourceWithPnr,
            rows.Count - sourceWithPnr,
            airlines,
            rows.Count(x => x.CheckIn.HasValue || x.CheckOut.HasValue),
            blocking,
            warnings,
            hash,
            canCommit,
            issues,
            suggestions,
            sourceWithPnr,
            rows.Count - sourceWithPnr,
            rows.Where(x => x.Pnr is not null).Select(x => x.Pnr).Distinct().Count(),
            rows.Count(x => x.Passport is not null),
            rows.Count(x => x.BirthDate.HasValue),
            rows.Count(x => x.PassportExpiry.HasValue),
            rows.Count(x => x.Nationality is not null));
        return new(rows, matches, bookings, trip.Id, hash, result);
    }

    private static List<PassengerTravelRow> ParseRows(byte[] bytes, string fileName, ICollection<PassengerTravelImportIssue> issues)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".csv" => ParseCsv(bytes, issues),
            ".xlsx" => ParseXlsx(bytes, issues),
            _ => throw new InvalidOperationException("Solo se permiten archivos CSV UTF-8 separados por punto y coma o XLSX.")
        };
    }

    private static List<PassengerTravelRow> ParseCsv(byte[] bytes, ICollection<PassengerTravelImportIssue> issues)
    {
        string content;
        try { content = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { throw new InvalidOperationException("El CSV debe estar codificado en UTF-8."); }
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) throw new InvalidOperationException("El CSV está vacío.");
        var headerValues = SplitSemicolonLine(lines[0]);
        var headers = headerValues.Select((value, index) => new { Key = value.Trim().ToLowerInvariant(), Index = index })
            .GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.First().Index);
        var missing = RequiredHeaders.Where(x => !headers.ContainsKey(x)).ToArray();
        if (missing.Length > 0) throw new InvalidOperationException("Faltan encabezados requeridos: " + string.Join(", ", missing));
        var rows = new List<PassengerTravelRow>();
        for (var index = 1; index < lines.Length; index++)
        {
            var values = SplitSemicolonLine(lines[index]);
            string? Value(string key) => headers[key] < values.Count ? Clean(values[headers[key]]) : null;
            var sourceRow = int.TryParse(Value("row"), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedRow) ? parsedRow : index;
            rows.Add(BuildRow(sourceRow, Value("name"), Value("passport"), Value("birth_date"), Value("passport_expiry"),
                Value("nationality_code"), Value("pnr"), Value("airline_code"), Value("check_in"), Value("check_out"), issues));
        }
        return rows;
    }

    private static List<PassengerTravelRow> ParseXlsx(byte[] bytes, ICollection<PassengerTravelImportIssue> issues)
    {
        if (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B)
            throw new InvalidOperationException("El contenido no es un XLSX válido.");
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var candidate = workbook.Worksheets.Select(sheet => new { Sheet = sheet, Header = FindHeader(sheet) })
            .FirstOrDefault(x => x.Header.HasValue)
            ?? throw new InvalidOperationException("No se encontró una hoja con encabezados de actualización de pasajeros.");
        var headerRow = candidate.Header!.Value;
        var headers = candidate.Sheet.Row(headerRow).CellsUsed()
            .GroupBy(x => NormalizeHeader(x.GetFormattedString()))
            .ToDictionary(x => x.Key, x => x.First().Address.ColumnNumber);
        string? Cell(IXLRow row, string key) => headers.TryGetValue(key, out var column) ? Clean(row.Cell(column).GetFormattedString()) : null;
        var rows = new List<PassengerTravelRow>();
        foreach (var row in candidate.Sheet.RowsUsed().Where(x => x.RowNumber() > headerRow))
        {
            var name = Cell(row, "name");
            if (name is null) continue;
            var sourceRow = int.TryParse(Cell(row, "row"), out var parsedRow) ? parsedRow : row.RowNumber();
            rows.Add(BuildRow(sourceRow, name, Cell(row, "passport"), Cell(row, "birth_date"), Cell(row, "passport_expiry"),
                Cell(row, "nationality_code"), Cell(row, "pnr"), Cell(row, "airline_code"), Cell(row, "check_in"), Cell(row, "check_out"), issues));
        }
        return rows;
    }

    private static PassengerTravelRow BuildRow(int row, string? name, string? passport, string? birthDate,
        string? passportExpiry, string? nationalityCode, string? pnr, string? airlineCode,
        string? checkIn, string? checkOut, ICollection<PassengerTravelImportIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(name)) issues.Add(new("Error", row, "name", "La fila no tiene nombre."));
        var birth = ParseDate(birthDate, row, "birth_date", issues);
        var expiry = ParseDate(passportExpiry, row, "passport_expiry", issues);
        var secondaryIn = ParseDate(checkIn, row, "check_in", issues);
        var secondaryOut = ParseDate(checkOut, row, "check_out", issues);
        var cleanPnr = NormalizePnr(pnr);
        var cleanAirlineCode = Clean(airlineCode)?.ToUpperInvariant();
        var airline = cleanAirlineCode switch
        {
            "CM" => "Copa Airlines (CM)",
            "LA" => "LATAM Airlines (LA)",
            null => null,
            _ => null
        };
        if (cleanAirlineCode is not null && airline is null)
            issues.Add(new("Error", row, "airline_code", "El código de aerolínea no está soportado.", MaskCode(cleanAirlineCode)));
        if ((cleanPnr is null) != (airline is null))
            issues.Add(new("Error", row, "pnr", "PNR y aerolínea deben informarse juntos.", MaskCode(cleanPnr)));
        return new(row, name?.Trim() ?? string.Empty, TextNormalizer.Normalize(name), Clean(passport),
            string.IsNullOrWhiteSpace(passport) ? null : TextNormalizer.Normalize(passport), birth, expiry,
            NormalizeNationality(nationalityCode), cleanPnr, airline, cleanAirlineCode, secondaryIn, secondaryOut);
    }

    private static void ValidateRows(IReadOnlyList<PassengerTravelRow> rows, IReadOnlyList<Passenger> passengers,
        ICollection<PassengerTravelImportIssue> issues)
    {
        foreach (var group in rows.GroupBy(x => x.Row).Where(x => x.Count() > 1))
            issues.Add(new("Error", group.Key, "row", "El número de fila aparece más de una vez."));
        foreach (var group in rows.GroupBy(x => x.NormalizedName).Where(x => x.Count() > 1))
            foreach (var row in group) issues.Add(new("Error", row.Row, "name", "El nombre aparece más de una vez en la fuente."));
        foreach (var group in rows.Where(x => x.NormalizedPassport is not null).GroupBy(x => x.NormalizedPassport).Where(x => x.Count() > 1))
            foreach (var row in group) issues.Add(new("Error", row.Row, "passport", "El pasaporte aparece más de una vez en la fuente.", MaskPassport(row.Passport)));
        foreach (var group in rows.Where(x => x.Pnr is not null).GroupBy(x => x.Pnr).Where(x => x.Select(r => r.Airline).Distinct().Count() > 1))
            foreach (var row in group) issues.Add(new("Error", row.Row, "pnr", "El mismo PNR tiene aerolíneas contradictorias.", MaskCode(row.Pnr)));
        foreach (var row in rows.Where(x => x.NormalizedPassport is not null))
        {
            var owners = passengers.Where(x => x.NormalizedPassportNumber == row.NormalizedPassport).ToArray();
            if (owners.Length > 1)
                issues.Add(new("Error", row.Row, "passport", "El pasaporte ya pertenece a más de un pasajero.", MaskPassport(row.Passport)));
        }
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var row in rows)
        {
            if (row.BirthDate > today)
                issues.Add(new("Error", row.Row, "birth_date", "La fecha de nacimiento no puede estar en el futuro."));
            if (row.BirthDate < today.AddYears(-120))
                issues.Add(new("Error", row.Row, "birth_date", "La fecha de nacimiento excede el rango admitido."));
            if (row.PassportExpiry.HasValue && row.BirthDate.HasValue && row.PassportExpiry < row.BirthDate)
                issues.Add(new("Error", row.Row, "passport_expiry", "El vencimiento del pasaporte no puede ser anterior al nacimiento."));
        }
    }

    private static Dictionary<int, Passenger> MatchPassengers(
        IReadOnlyList<PassengerTravelRow> rows,
        IReadOnlyList<Passenger> passengers,
        IReadOnlyDictionary<int, Guid>? aliases,
        ICollection<PassengerTravelImportIssue> issues,
        out IReadOnlyList<PassengerTravelMatchSuggestion> suggestions,
        out int ambiguous)
    {
        var byName = passengers.GroupBy(x => x.NormalizedName).ToDictionary(x => x.Key, x => x.ToArray());
        var byId = passengers.ToDictionary(x => x.Id);
        var matches = new Dictionary<int, Passenger>();
        var proposed = new List<PassengerTravelMatchSuggestion>();
        ambiguous = 0;
        foreach (var row in rows)
        {
            if (byName.TryGetValue(row.NormalizedName, out var exact) && exact.Length == 1)
            {
                matches[row.Row] = exact[0];
                continue;
            }
            if (aliases is not null && aliases.TryGetValue(row.Row, out var passengerId) && byId.TryGetValue(passengerId, out var alias))
            {
                matches[row.Row] = alias;
                continue;
            }
            var candidates = Suggest(row.NormalizedName, passengers).ToArray();
            foreach (var candidate in candidates)
                proposed.Add(new(row.Row, candidate.Id, MaskName(candidate.FullName)));
            if (candidates.Length > 1)
            {
                ambiguous++;
                issues.Add(new("Error", row.Row, "name", "La fila coincide con más de un candidato; requiere selección manual."));
            }
            else
            {
                issues.Add(new("Error", row.Row, "name", candidates.Length == 1
                    ? "La coincidencia sugerida debe confirmarse manualmente."
                    : "No se encontró un pasajero coincidente."));
            }
        }
        suggestions = proposed;
        return matches;
    }

    private static IEnumerable<Passenger> Suggest(string source, IEnumerable<Passenger> passengers)
    {
        var collapsed = source.Replace(" ", string.Empty, StringComparison.Ordinal);
        var values = passengers.ToArray();
        var strong = values.Where(passenger =>
        {
            var candidate = passenger.NormalizedName;
            return candidate.Replace(" ", string.Empty, StringComparison.Ordinal) == collapsed
                || candidate.StartsWith(source + " ", StringComparison.Ordinal)
                || source.StartsWith(candidate + " ", StringComparison.Ordinal)
                || Levenshtein(candidate, source) <= 2;
        }).ToArray();
        if (strong.Length > 0) return strong;

        var ranked = values.Select(passenger => new
        {
            Passenger = passenger,
            Distance = Levenshtein(passenger.NormalizedName, source),
            Length = Math.Max(passenger.NormalizedName.Length, source.Length)
        }).OrderBy(x => x.Distance).ToArray();
        if (ranked.Length == 0) return [];
        var best = ranked[0];
        var nextDistance = ranked.Length > 1 ? ranked[1].Distance : int.MaxValue;
        return best.Distance <= Math.Max(3, (int)Math.Ceiling(best.Length * 0.2d))
               && nextDistance - best.Distance >= 3
            ? [best.Passenger]
            : [];
    }

    private static int Levenshtein(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1];
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[right.Length];
    }

    private static int? FindHeader(IXLWorksheet sheet)
    {
        foreach (var row in sheet.RowsUsed())
        {
            var values = row.CellsUsed().Select(x => NormalizeHeader(x.GetFormattedString())).ToHashSet();
            if (RequiredHeaders.All(values.Contains)) return row.RowNumber();
        }
        return null;
    }

    private static string NormalizeHeader(string value) => TextNormalizer.Normalize(value).ToLowerInvariant()
        .Replace(' ', '_');

    private static IReadOnlyList<string> SplitSemicolonLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"') { current.Append('"'); index++; }
                else quoted = !quoted;
            }
            else if (character == ';' && !quoted) { values.Add(current.ToString()); current.Clear(); }
            else current.Append(character);
        }
        if (quoted) throw new InvalidOperationException("El CSV contiene comillas sin cerrar.");
        values.Add(current.ToString());
        return values;
    }

    private static DateOnly? ParseDate(string? value, int row, string field, ICollection<PassengerTravelImportIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime)) return DateOnly.FromDateTime(dateTime);
        issues.Add(new("Error", row, field, "La fecha no es válida; usá YYYY-MM-DD."));
        return null;
    }

    private static string? NormalizeNationality(string? value) => TextNormalizer.Normalize(value) switch
    {
        "PYA" => "Paraguay",
        "ITA" => "Italia",
        "MEX" => "México",
        "EEUU" => "Estados Unidos",
        "" => null,
        _ => Clean(value)
    };

    private static string? NormalizePnr(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    private static string? AppendReference(string? existing, string value) => string.IsNullOrWhiteSpace(existing)
        ? value
        : ContainsReference(existing, value) ? existing : $"{existing}; {value}";
    private static bool ContainsReference(string? existing, string value) => existing?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string SafeFileName(string value)
    {
        var name = Path.GetFileName(value);
        return name.Length <= 180 ? name : name[..180];
    }
    private static string? MaskCode(string? value) => string.IsNullOrWhiteSpace(value) ? null : $"***{value[^Math.Min(2, value.Length)..]}";
    private static string? MaskPassport(string? value) => string.IsNullOrWhiteSpace(value) ? null : PassengerQueryService.MaskPassport(value);
    private static string MaskName(string value) => string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => $"{x[0]}***"));

    private static void CompareIdentity<T>(T? existing, T? incoming, ref int complete, ref int update)
    {
        if (IsEmpty(incoming) || EqualityComparer<T?>.Default.Equals(existing, incoming)) return;
        if (IsEmpty(existing)) complete++; else update++;
    }

    private static bool ApplyValue<T>(T? existing, T? incoming, bool overwriteExisting, Action<T?> setter)
    {
        if (IsEmpty(incoming) || EqualityComparer<T?>.Default.Equals(existing, incoming) || !IsEmpty(existing) && !overwriteExisting) return false;
        setter(incoming);
        return true;
    }

    private static bool ApplyString(string? existing, string? incoming, bool overwriteExisting, Action<string?> setter)
    {
        if (string.IsNullOrWhiteSpace(incoming) || string.Equals(existing, incoming, StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(existing) && !overwriteExisting) return false;
        setter(incoming);
        return true;
    }

    private static bool IsEmpty<T>(T? value) => value is null || value is string text && string.IsNullOrWhiteSpace(text);

    private sealed record ParsedPassengerTravel(
        IReadOnlyList<PassengerTravelRow> Rows,
        IReadOnlyDictionary<int, Passenger> Matches,
        IReadOnlyList<FlightBooking> Bookings,
        Guid TripId,
        string Hash,
        PassengerTravelImportResult Result);
}
