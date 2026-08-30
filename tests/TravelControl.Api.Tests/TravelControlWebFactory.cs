using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TravelControl.Infrastructure.Persistence;

namespace TravelControl.Api.Tests;

public sealed class TravelControlWebFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly string _keysPath = Path.Combine(AppContext.BaseDirectory, $"travel-control-keys-{Guid.NewGuid():N}");
    private readonly string _attachmentsPath = Path.Combine(AppContext.BaseDirectory, $"travel-control-attachments-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();
        Directory.CreateDirectory(_keysPath);
        Directory.CreateDirectory(_attachmentsPath);
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:CookieSecure"] = "false",
            ["Security:DataProtectionKeys"] = _keysPath,
            ["Storage:Root"] = _attachmentsPath,
            ["BootstrapImport:Enabled"] = "false",
            ["BootstrapImport:Required"] = "false",
            ["PublicRead:Enabled"] = "true",
            ["PublicRead:NameMode"] = "Full"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton(_connection);
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            services.AddDbContext<AppDbContext>((provider, options) => options.UseSqlite(provider.GetRequiredService<SqliteConnection>()));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
            var resolvedKeysPath = Path.GetFullPath(_keysPath);
            if (Directory.Exists(resolvedKeysPath)
                && string.Equals(Path.GetDirectoryName(resolvedKeysPath), Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(resolvedKeysPath).StartsWith("travel-control-keys-", StringComparison.Ordinal))
                Directory.Delete(resolvedKeysPath, recursive: true);
            var resolvedAttachmentsPath = Path.GetFullPath(_attachmentsPath);
            if (Directory.Exists(resolvedAttachmentsPath)
                && string.Equals(Path.GetDirectoryName(resolvedAttachmentsPath), Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(resolvedAttachmentsPath).StartsWith("travel-control-attachments-", StringComparison.Ordinal))
                Directory.Delete(resolvedAttachmentsPath, recursive: true);
        }
    }
}
