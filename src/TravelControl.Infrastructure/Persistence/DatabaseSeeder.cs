using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TravelControl.Domain;
using TravelControl.Infrastructure.Services;

namespace TravelControl.Infrastructure.Persistence;

public sealed class BootstrapImportOptions
{
    public bool Enabled { get; init; }
    public bool Required { get; init; }
    public string MasterWorkbookPath { get; init; } = "/var/lib/travel-control/private/Control_viaje.xlsx";
    public string? GuestListPath { get; init; }
    public string? IdentificationPath { get; init; }
}

public static class DatabaseSeeder
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<AppDbContext>();
        var configuration = provider.GetRequiredService<IConfiguration>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseBootstrap");
        await db.Database.MigrateAsync(ct);

        var trip = await db.Trips.Include(x => x.TransferStatus).SingleOrDefaultAsync(x => x.IsActive, ct);
        if (trip is null)
        {
            trip = new Trip
            {
                Name = "Viaje grupal",
                Destination = "Riviera Maya, México",
                StartDate = new DateOnly(2026, 9, 6),
                EndDate = new DateOnly(2026, 9, 15),
                WeddingDate = new DateOnly(2026, 9, 9),
                TimeZone = "America/Cancun",
                PassportWarningDays = 180,
                TransferStatus = new TripTransferStatus { IsConfirmed = false }
            };
            db.Trips.Add(trip);
        }
        else if (trip.TransferStatus is null)
        {
            trip.TransferStatus = new TripTransferStatus { TripId = trip.Id, IsConfirmed = false };
        }

        foreach (var item in new[]
        {
            new Operator { Name = "Top Travel", Type = OperatorType.Agency },
            new Operator { Name = "Bespoke", Type = OperatorType.HotelOperator, Phone = "+595 21 608 508" }
        })
            if (!await db.Operators.AnyAsync(x => x.Name == item.Name, ct)) db.Operators.Add(item);
        await db.SaveChangesAsync(ct);

        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in Enum.GetNames<UserRole>())
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
                if (!result.Succeeded) throw new InvalidOperationException($"No se pudo crear el rol {role}.");
            }

        if (await db.Passengers.AnyAsync(x => x.TripId == trip.Id, ct)) return;
        var options = configuration.GetSection("BootstrapImport").Get<BootstrapImportOptions>() ?? new BootstrapImportOptions();
        if (!options.Enabled)
        {
            if (options.Required) throw new InvalidOperationException("BootstrapImport está marcado como requerido pero no está habilitado.");
            return;
        }

        var masterPath = Path.GetFullPath(options.MasterWorkbookPath);
        if (!File.Exists(masterPath))
        {
            if (options.Required) throw new FileNotFoundException("La base está vacía y falta el workbook privado requerido para el bootstrap.", masterPath);
            logger.LogWarning("Bootstrap skipped: master workbook is not available and the database is empty.");
            return;
        }

        var importer = provider.GetRequiredService<ExcelImportService>();
        await using (var previewStream = File.OpenRead(masterPath))
        {
            var preview = await importer.ProcessAsync(previewStream, Path.GetFileName(masterPath), true, null, ct);
            if (!preview.CanCommit || preview.PassengerRows != 46 || preview.RoomRows != 25)
                throw new InvalidOperationException($"El dry-run del bootstrap falló: pasajeros={preview.PassengerRows}, habitaciones={preview.RoomRows}, errores={preview.Errors}.");
        }
        await using (var importStream = File.OpenRead(masterPath))
        {
            var committed = await importer.ProcessAsync(importStream, Path.GetFileName(masterPath), false, null, ct);
            if (!committed.CanCommit) throw new InvalidOperationException("El bootstrap privado no pudo confirmarse.");
        }

        await EnrichIfAvailable(importer, options.GuestListPath, EnrichmentKind.GuestList, ct);
        await EnrichIfAvailable(importer, options.IdentificationPath, EnrichmentKind.Identification, ct);
        logger.LogInformation("Private bootstrap completed. passengers={Passengers}; rooms={Rooms}", 46, 25);
    }

    private static async Task EnrichIfAvailable(ExcelImportService importer, string? path, EnrichmentKind kind, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) return;
        await using var stream = File.OpenRead(fullPath);
        await importer.EnrichAsync(stream, kind, null, ct);
    }
}
