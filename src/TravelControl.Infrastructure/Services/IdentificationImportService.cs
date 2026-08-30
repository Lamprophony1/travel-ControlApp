using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using TravelControl.Application.Services;
using TravelControl.Domain;
using TravelControl.Infrastructure.Persistence;

namespace TravelControl.Infrastructure.Services;

public sealed record IdentificationImportIssue(
    string Level,
    int? Row,
    string? Field,
    string Message,
    string? PassportReference = null,
    bool WillOverwrite = false);

public sealed record IdentificationImportResult(
    int RowsRead,
    int Matched,
    int Unmatched,
    int Duplicates,
    int Unchanged,
    int MissingFields,
    int Conflicts,
    int InvalidDates,
    int DuplicatePassports,
    int BlockingErrors,
    int Warnings,
    int WillUpdate,
    int WillOverwrite,
    bool CanCommit,
    IReadOnlyList<IdentificationImportIssue> Issues,
    Guid? ImportRunId = null,
    string? SelectedSheet = null,
    IReadOnlyList<string>? CandidateSheets = null,
    int ExpiredPassports = 0,
    int ExpiriesBeforeReturn = 0,
    int ExpiriesWithinWarningThreshold = 0,
    int TemporallyInconsistentRows = 0);

public sealed record IdentificationQuality(
    int TotalPassengers,
    int CompletePassports,
    int IncompletePassports,
    int BirthDates,
    int Nationalities,
    int PassportExpiries,
    int DuplicatePassports);

internal sealed record IdentificationRow(
    int Row,
    string Name,
    string NormalizedName,
    string? Passport,
    string? NormalizedPassport,
    DateOnly? BirthDate,
    string? Nationality,
    DateOnly? PassportExpiry,
    bool BirthDateInvalid,
    bool PassportExpiryInvalid,
    bool BirthDateAmbiguous,
    bool PassportExpiryAmbiguous);

public sealed class IdentificationImportService(AppDbContext db, ILogger<IdentificationImportService> logger)
{
    private static readonly string[] NameHeaders = ["NOMBRE", "NOMBRE COMPLETO", "PASAJERO", "NOMBRE Y APELLIDO"];
    private static readonly string[] PassportHeaders = ["PASAPORTE", "NRO DE PASP", "NRO PASAPORTE", "NUMERO DE PASAPORTE"];
    private static readonly string[] BirthHeaders = ["FECHA NAC", "FECHA DE NACIMIENTO", "NACIMIENTO"];
    private static readonly string[] ExpiryHeaders = ["FECHA VENC", "FECHA DE VENCIMIENTO", "VENCIMIENTO", "VENCIMIENTO PASAPORTE"];
    private static readonly string[] NationalityHeaders = ["NAC", "NACIONALIDAD"];

    public async Task<IdentificationImportResult> PreviewAsync(
        Stream stream,
        string fileName,
        bool overwriteExisting,
        CancellationToken ct,
        string? sheetName = null)
    {
        var parsed = await ParseAsync(stream, fileName, overwriteExisting, sheetName, ct);
        return parsed.Result;
    }

    public async Task<IdentificationImportResult> CommitAsync(
        Stream stream,
        string fileName,
        bool overwriteExisting,
        bool overwriteConfirmed,
        Guid userId,
        string? userName,
        CancellationToken ct,
        string? sheetName = null)
    {
        if (overwriteExisting && !overwriteConfirmed)
            throw new InvalidOperationException("Confirmá expresamente la sobrescritura de valores existentes.");
        var parsed = await ParseAsync(stream, fileName, overwriteExisting, sheetName, ct);
        if (!parsed.Result.CanCommit) return parsed.Result;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var updatedPassengers = 0;
        var updatedFields = 0;
        foreach (var row in parsed.Rows)
        {
            if (!parsed.Passengers.TryGetValue(row.NormalizedName, out var passenger)) continue;
            var changes = new Dictionary<string, object?>();
            var previous = new Dictionary<string, object?>();

            Apply(
                "BirthDate",
                passenger.BirthDate,
                row.BirthDate,
                overwriteExisting,
                value => passenger.BirthDate = value,
                previous,
                changes,
                value => value);
            Apply(
                "Nationality",
                passenger.Nationality,
                row.Nationality,
                overwriteExisting,
                value => passenger.Nationality = value,
                previous,
                changes,
                value => value);
            Apply(
                "PassportNumber",
                passenger.PassportNumber,
                row.Passport,
                overwriteExisting,
                value =>
                {
                    passenger.PassportNumber = value;
                    passenger.NormalizedPassportNumber = string.IsNullOrWhiteSpace(value) ? null : TextNormalizer.Normalize(value);
                },
                previous,
                changes,
                PassengerQueryService.MaskPassport);
            Apply(
                "PassportExpiry",
                passenger.PassportExpiry,
                row.PassportExpiry,
                overwriteExisting,
                value => passenger.PassportExpiry = value,
                previous,
                changes,
                value => value);

            if (changes.Count == 0) continue;
            passenger.UpdatedById = userId;
            updatedPassengers++;
            updatedFields += changes.Count;
            db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                UserName = userName,
                EntityName = "Passenger",
                EntityId = passenger.Id.ToString(),
                PassengerId = passenger.Id,
                Action = "IdentificationImport",
                PreviousValue = JsonSerializer.Serialize(previous),
                NewValue = JsonSerializer.Serialize(changes)
            });
        }

        await db.SaveChangesAsync(ct);
        var run = new ImportRun
        {
            FileName = SafeFileName(fileName),
            Sha256 = parsed.Hash,
            DryRun = false,
            ImportType = "Identification",
            Status = "Completado",
            Matched = parsed.Result.Matched,
            Conflicts = parsed.Result.Conflicts,
            Added = 0,
            Updated = updatedPassengers,
            Unchanged = parsed.Result.Matched - updatedPassengers,
            Errors = 0,
            SummaryJson = JsonSerializer.Serialize(new
            {
                parsed.Result.RowsRead,
                parsed.Result.Matched,
                parsed.Result.Unmatched,
                parsed.Result.Conflicts,
                updatedPassengers,
                updatedFields,
                overwriteExisting
            }),
            UserId = userId
        };
        db.ImportRuns.Add(run);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation(
            "Identification import completed. matched={Matched}; updatedPassengers={UpdatedPassengers}; updatedFields={UpdatedFields}; conflicts={Conflicts}",
            parsed.Result.Matched,
            updatedPassengers,
            updatedFields,
            parsed.Result.Conflicts);
        return parsed.Result with { WillUpdate = updatedPassengers, ImportRunId = run.Id };
    }

    public async Task<IdentificationQuality> GetQualityAsync(CancellationToken ct)
    {
        var values = await db.Passengers.AsNoTracking()
            .Select(x => new { x.PassportNumber, x.NormalizedPassportNumber, x.BirthDate, x.Nationality, x.PassportExpiry })
            .ToListAsync(ct);
        var duplicatePassports = values.Where(x => !string.IsNullOrWhiteSpace(x.NormalizedPassportNumber))
            .GroupBy(x => x.NormalizedPassportNumber)
            .Count(x => x.Count() > 1);
        var complete = values.Count(x => !string.IsNullOrWhiteSpace(x.PassportNumber)
            && x.BirthDate.HasValue && !string.IsNullOrWhiteSpace(x.Nationality) && x.PassportExpiry.HasValue);
        return new(
            values.Count,
            complete,
            values.Count - complete,
            values.Count(x => x.BirthDate.HasValue),
            values.Count(x => !string.IsNullOrWhiteSpace(x.Nationality)),
            values.Count(x => x.PassportExpiry.HasValue),
            duplicatePassports);
    }

    private async Task<ParsedIdentification> ParseAsync(Stream stream, string fileName, bool overwriteExisting,
        string? sheetName, CancellationToken ct)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Solo se permiten archivos XLSX.");
        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        if (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B)
            throw new InvalidOperationException("El contenido no es un XLSX válido.");

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var candidates = workbook.Worksheets.Select(sheet => new
            { Sheet = sheet, Header = FindHeader(sheet), Score = SheetScore(sheet) })
            .Where(x => x.Header.HasValue).ToArray();
        var candidateNames = candidates.Select(x => x.Sheet.Name).ToArray();
        var selected = SelectSheet(candidates.Select(x => (x.Sheet, Header: x.Header!.Value, x.Score)).ToArray(), sheetName);
        var issues = new List<IdentificationImportIssue>();
        if (selected is null)
        {
            issues.Add(new("Error", null, null, "No se encontró una columna compatible de nombre."));
            return new([], new Dictionary<string, Passenger>(), Convert.ToHexString(SHA256.HashData(bytes)),
                BuildResult([], 0, 0, 0, 0, 0, 0, 0, 0, 1, issues, overwriteExisting)
                    with { CandidateSheets = candidateNames });
        }

        var rows = ParseRows(selected.Value.Sheet, selected.Value.Header);
        var trip = await db.Trips.AsNoTracking().SingleAsync(x => x.IsActive, ct);
        var tripId = trip.Id;
        var passengerList = await db.Passengers.Where(x => x.TripId == tripId).ToListAsync(ct);
        var passengers = passengerList.ToDictionary(x => x.NormalizedName);
        var duplicateNames = rows.GroupBy(x => x.NormalizedName).Where(x => x.Count() > 1).ToArray();
        foreach (var group in duplicateNames)
            foreach (var row in group)
                issues.Add(new("Error", row.Row, "Name", "El nombre normalizado aparece más de una vez en el archivo."));

        foreach (var row in rows.Where(x => x.BirthDateInvalid))
            issues.Add(new("Error", row.Row, "BirthDate", "La fecha de nacimiento no es válida."));
        foreach (var row in rows.Where(x => x.PassportExpiryInvalid))
            issues.Add(new("Error", row.Row, "PassportExpiry", "La fecha de vencimiento no es válida."));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var row in rows)
        {
            if (row.BirthDate > today)
                issues.Add(new("Error", row.Row, "BirthDate", "La fecha de nacimiento no puede ser futura."));
            if (row.BirthDate < today.AddYears(-120))
                issues.Add(new("Error", row.Row, "BirthDate", "La edad calculada supera 120 años."));
            if (row.BirthDate.HasValue && row.PassportExpiry.HasValue && row.PassportExpiry < row.BirthDate)
                issues.Add(new("Error", row.Row, "PassportExpiry", "El vencimiento no puede ser anterior al nacimiento."));
            if (row.PassportExpiry < today)
                issues.Add(new("Advertencia", row.Row, "PassportExpiry", "Pasaporte vencido. Control preventivo interno; verificar requisitos migratorios oficiales."));
            if (row.PassportExpiry >= today && row.PassportExpiry < trip.EndDate)
                issues.Add(new("Advertencia", row.Row, "PassportExpiry", "El pasaporte vence antes del regreso. Control preventivo interno; verificar requisitos migratorios oficiales."));
            else if (row.PassportExpiry >= trip.EndDate && row.PassportExpiry <= trip.EndDate.AddDays(trip.PassportWarningDays))
                issues.Add(new("Advertencia", row.Row, "PassportExpiry", "El vencimiento está dentro del umbral preventivo. Control preventivo interno; verificar requisitos migratorios oficiales."));
            if (row.BirthDateAmbiguous)
                issues.Add(new("Advertencia", row.Row, "BirthDate", "Formato de fecha ambiguo resuelto con cultura es-PY."));
            if (row.PassportExpiryAmbiguous)
                issues.Add(new("Advertencia", row.Row, "PassportExpiry", "Formato de fecha ambiguo resuelto con cultura es-PY."));
        }

        var duplicatePassportRows = rows.Where(x => !string.IsNullOrWhiteSpace(x.NormalizedPassport))
            .GroupBy(x => x.NormalizedPassport)
            .Where(x => x.Count() > 1)
            .SelectMany(x => x)
            .ToArray();
        foreach (var row in duplicatePassportRows)
            issues.Add(new("Error", row.Row, "PassportNumber", "El pasaporte aparece más de una vez en el archivo.", PassengerQueryService.MaskPassport(row.Passport)));

        var matched = 0;
        var unmatched = 0;
        var unchanged = 0;
        var missingFields = 0;
        var conflicts = 0;
        var willUpdate = 0;
        var willOverwrite = 0;
        foreach (var row in rows)
        {
            if (!passengers.TryGetValue(row.NormalizedName, out var passenger))
            {
                unmatched++;
                issues.Add(new("Advertencia", row.Row, "Name", "No existe un pasajero coincidente; la fila no será importada."));
                continue;
            }
            matched++;
            var rowChanges = 0;
            Compare("BirthDate", passenger.BirthDate, row.BirthDate, row.Row, overwriteExisting, issues, ref missingFields, ref conflicts, ref rowChanges, ref willOverwrite, value => value?.ToString("dd/MM/yyyy"));
            Compare("Nationality", passenger.Nationality, row.Nationality, row.Row, overwriteExisting, issues, ref missingFields, ref conflicts, ref rowChanges, ref willOverwrite, value => value);
            Compare("PassportNumber", passenger.PassportNumber, row.Passport, row.Row, overwriteExisting, issues, ref missingFields, ref conflicts, ref rowChanges, ref willOverwrite, PassengerQueryService.MaskPassport);
            Compare("PassportExpiry", passenger.PassportExpiry, row.PassportExpiry, row.Row, overwriteExisting, issues, ref missingFields, ref conflicts, ref rowChanges, ref willOverwrite, value => value?.ToString("dd/MM/yyyy"));
            if (rowChanges == 0) unchanged++; else willUpdate++;

            if (!string.IsNullOrWhiteSpace(row.NormalizedPassport))
            {
                var owner = passengerList.FirstOrDefault(x => x.Id != passenger.Id && x.NormalizedPassportNumber == row.NormalizedPassport);
                if (owner is not null)
                    issues.Add(new("Error", row.Row, "PassportNumber", "El pasaporte ya está asignado a otro pasajero.", PassengerQueryService.MaskPassport(row.Passport)));
            }
        }

        var duplicatePassports = issues.Count(x => x.Field == "PassportNumber" && x.Level == "Error");
        var invalidDates = issues.Count(x => x.Field is "BirthDate" or "PassportExpiry" && x.Level == "Error");
        var blocking = issues.Count(x => x.Level == "Error");
        var result = BuildResult(rows, matched, unmatched, duplicateNames.Sum(x => x.Count()), unchanged, missingFields, conflicts,
            invalidDates, duplicatePassports, blocking, issues, overwriteExisting, willUpdate, willOverwrite) with
        {
            SelectedSheet = selected.Value.Sheet.Name,
            CandidateSheets = candidateNames,
            ExpiredPassports = rows.Count(x => x.PassportExpiry < today),
            ExpiriesBeforeReturn = rows.Count(x => x.PassportExpiry >= today && x.PassportExpiry < trip.EndDate),
            ExpiriesWithinWarningThreshold = rows.Count(x => x.PassportExpiry >= trip.EndDate
                && x.PassportExpiry <= trip.EndDate.AddDays(trip.PassportWarningDays)),
            TemporallyInconsistentRows = issues.Where(x => x.Level == "Error" && x.Field is "BirthDate" or "PassportExpiry")
                .Select(x => x.Row).Where(x => x.HasValue).Distinct().Count()
        };
        return new(rows, passengers, Convert.ToHexString(SHA256.HashData(bytes)), result);
    }

    private static IdentificationImportResult BuildResult(
        IReadOnlyList<IdentificationRow> rows,
        int matched,
        int unmatched,
        int duplicates,
        int unchanged,
        int missingFields,
        int conflicts,
        int invalidDates,
        int duplicatePassports,
        int blocking,
        IReadOnlyList<IdentificationImportIssue> issues,
        bool overwriteExisting,
        int willUpdate = 0,
        int willOverwrite = 0) => new(
            rows.Count,
            matched,
            unmatched,
            duplicates,
            unchanged,
            missingFields,
            conflicts,
            invalidDates,
            duplicatePassports,
            blocking,
            issues.Count(x => x.Level == "Advertencia"),
            willUpdate,
            overwriteExisting ? willOverwrite : 0,
            blocking == 0,
            issues);

    private static List<IdentificationRow> ParseRows(IXLWorksheet sheet, int headerRow)
    {
        var headers = sheet.Row(headerRow).CellsUsed()
            .GroupBy(x => TextNormalizer.Normalize(x.GetFormattedString()))
            .ToDictionary(x => x.Key, x => x.First().Address.ColumnNumber);
        var rows = new List<IdentificationRow>();
        foreach (var row in sheet.RowsUsed().Where(x => x.RowNumber() > headerRow))
        {
            var name = Value(row, headers, NameHeaders);
            if (string.IsNullOrWhiteSpace(name)) continue;
            var passport = Value(row, headers, PassportHeaders);
            var (birthDate, birthPresent, birthInvalid, birthAmbiguous) = Date(row, headers, BirthHeaders);
            var (expiry, expiryPresent, expiryInvalid, expiryAmbiguous) = Date(row, headers, ExpiryHeaders);
            rows.Add(new(
                row.RowNumber(),
                name.Trim(),
                TextNormalizer.Normalize(name),
                Clean(passport),
                string.IsNullOrWhiteSpace(passport) ? null : TextNormalizer.Normalize(passport),
                birthDate,
                Clean(Value(row, headers, NationalityHeaders)),
                expiry,
                birthPresent && birthInvalid,
                expiryPresent && expiryInvalid,
                birthAmbiguous,
                expiryAmbiguous));
        }
        return rows;
    }

    private static (IXLWorksheet Sheet, int Header, int Score)? SelectSheet(
        IReadOnlyList<(IXLWorksheet Sheet, int Header, int Score)> candidates,
        string? requestedName)
    {
        if (!string.IsNullOrWhiteSpace(requestedName))
            return candidates.FirstOrDefault(x => string.Equals(x.Sheet.Name.Trim(), requestedName.Trim(), StringComparison.OrdinalIgnoreCase)) is var requested
                && requested.Sheet is not null ? requested : null;
        static int Priority(string name)
        {
            var normalized = TextNormalizer.Normalize(name);
            if (normalized.StartsWith("IDENTIFICACI", StringComparison.Ordinal)) return 0;
            if (normalized == "PASAPORTES") return 1;
            if (normalized == "DOCUMENTOS") return 2;
            if (normalized == "DATOS PASAJEROS") return 3;
            if (normalized == "DATOS DE PASAJEROS") return 4;
            return int.MaxValue;
        }
        var prioritized = candidates.OrderBy(x => Priority(x.Sheet.Name)).FirstOrDefault();
        if (prioritized.Sheet is not null && Priority(prioritized.Sheet.Name) < int.MaxValue) return prioritized;
        if (candidates.Count == 0) return null;
        var maximum = candidates.Max(x => x.Score);
        var best = candidates.Where(x => x.Score == maximum).ToArray();
        return best.Length == 1 ? best[0] : null;
    }

    private static int SheetScore(IXLWorksheet sheet)
    {
        var header = FindHeader(sheet);
        if (!header.HasValue) return 0;
        var values = sheet.Row(header.Value).CellsUsed().Select(x => TextNormalizer.Normalize(x.GetFormattedString())).ToHashSet();
        static bool Has(IReadOnlySet<string> values, IEnumerable<string> aliases) => aliases.Any(x => values.Contains(TextNormalizer.Normalize(x)));
        return (Has(values, NameHeaders) ? 1 : 0) + (Has(values, PassportHeaders) ? 1 : 0)
            + (Has(values, BirthHeaders) ? 1 : 0) + (Has(values, NationalityHeaders) ? 1 : 0)
            + (Has(values, ExpiryHeaders) ? 1 : 0);
    }

    private static int? FindHeader(IXLWorksheet sheet)
    {
        foreach (var row in sheet.RowsUsed())
        {
            var headers = row.CellsUsed().Select(x => TextNormalizer.Normalize(x.GetFormattedString())).ToHashSet();
            if (NameHeaders.Any(x => headers.Contains(TextNormalizer.Normalize(x)))) return row.RowNumber();
        }
        return null;
    }

    private static string? Value(IXLRow row, IReadOnlyDictionary<string, int> headers, IEnumerable<string> aliases)
    {
        foreach (var alias in aliases)
            if (headers.TryGetValue(TextNormalizer.Normalize(alias), out var column))
            {
                var value = row.Cell(column).GetFormattedString().Trim();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        return null;
    }

    private static (DateOnly? Value, bool Present, bool Invalid, bool Ambiguous) Date(
        IXLRow row,
        IReadOnlyDictionary<string, int> headers,
        IEnumerable<string> aliases)
    {
        foreach (var alias in aliases)
        {
            if (!headers.TryGetValue(TextNormalizer.Normalize(alias), out var column)) continue;
            var cell = row.Cell(column);
            if (cell.IsEmpty()) return (null, false, false, false);
            if (cell.TryGetValue<DateTime>(out var dateTime)) return (DateOnly.FromDateTime(dateTime), true, false, false);
            var text = cell.GetFormattedString().Trim();
            if (DateOnly.TryParse(text, CultureInfo.GetCultureInfo("es-PY"), DateTimeStyles.None, out var localized))
            {
                var ambiguous = DateOnly.TryParse(text, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var alternate)
                    && alternate != localized;
                return (localized, true, false, ambiguous);
            }
            if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out localized))
                return (localized, true, false, false);
            return (null, true, true, false);
        }
        return (null, false, false, false);
    }

    private static void Compare<T>(
        string field,
        T? existing,
        T? incoming,
        int row,
        bool overwriteExisting,
        ICollection<IdentificationImportIssue> issues,
        ref int missingFields,
        ref int conflicts,
        ref int rowChanges,
        ref int willOverwrite,
        Func<T?, string?> display)
    {
        if (IsEmpty(incoming)) return;
        if (IsEmpty(existing))
        {
            missingFields++;
            rowChanges++;
            return;
        }
        if (EqualityComparer<T?>.Default.Equals(existing, incoming)) return;
        conflicts++;
        if (overwriteExisting)
        {
            rowChanges++;
            willOverwrite++;
        }
        issues.Add(new(
            "Advertencia",
            row,
            field,
            overwriteExisting ? "El valor existente será sobrescrito tras la confirmación adicional." : "El valor existente se conservará.",
            field == "PassportNumber" ? display(incoming) : null,
            overwriteExisting));
    }

    private static void Apply<T>(
        string field,
        T? existing,
        T? incoming,
        bool overwriteExisting,
        Action<T?> setter,
        IDictionary<string, object?> previous,
        IDictionary<string, object?> changes,
        Func<T?, object?> audit)
    {
        if (IsEmpty(incoming) || (!IsEmpty(existing) && !overwriteExisting) || EqualityComparer<T?>.Default.Equals(existing, incoming)) return;
        previous[field] = audit(existing);
        changes[field] = audit(incoming);
        setter(incoming);
    }

    private static bool IsEmpty<T>(T? value) => value is null || value is string text && string.IsNullOrWhiteSpace(text);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string SafeFileName(string value)
    {
        var fileName = Path.GetFileName(value);
        return fileName.Length <= 180 ? fileName : fileName[..180];
    }

    private sealed record ParsedIdentification(
        IReadOnlyList<IdentificationRow> Rows,
        Dictionary<string, Passenger> Passengers,
        string Hash,
        IdentificationImportResult Result);
}
