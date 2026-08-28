using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TravelControl.Api.Domain;

namespace TravelControl.Api.Data;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        if (!await db.Trips.AnyAsync())
        {
            db.Trips.Add(new Trip
            {
                Name = "Boda Cielito & Ronaldo",
                Destination = "Riviera Maya, México",
                StartDate = new DateOnly(2026, 9, 6),
                EndDate = new DateOnly(2026, 9, 15),
                WeddingDate = new DateOnly(2026, 9, 9),
                TimeZone = "America/Cancun",
                PassportWarningDays = 180
            });
        }

        foreach (var item in new[]
        {
            new Operator { Name = "Top Travel", Type = OperatorType.Agency },
            new Operator { Name = "Bespoke", Type = OperatorType.HotelOperator, Phone = "595 21 608-508" }
        })
        {
            if (!await db.Operators.AnyAsync(x => x.Name == item.Name)) db.Operators.Add(item);
        }

        await db.SaveChangesAsync();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in Enum.GetNames<UserRole>())
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
    }
}

