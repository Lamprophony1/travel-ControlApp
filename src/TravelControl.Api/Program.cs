using FluentValidation;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Serilog;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using TravelControl.Api.Endpoints;
using TravelControl.Domain;
using TravelControl.Infrastructure.Identity;
using TravelControl.Infrastructure.Persistence;
using TravelControl.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, cfg) => cfg.ReadFrom.Configuration(context.Configuration).WriteTo.Console());
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(options => options.AddSecurityDefinition("cookieAuth", new OpenApiSecurityScheme
{
    Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Cookie, Name = ".TravelControl.Auth",
    Description = "Cookie HttpOnly de sesión"
}));
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var connection = builder.Configuration.GetConnectionString("Database") ?? "Data Source=/var/lib/travel-control/data/travel-control.db";
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(
    builder.Configuration["Security:DataProtectionKeys"] ?? "/var/lib/travel-control/keys"));
builder.Services.AddIdentityCore<AppUser>(options =>
{
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.User.RequireUniqueEmail = true;
}).AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<AppDbContext>().AddSignInManager().AddDefaultTokenProviders();
var secureCookies = builder.Configuration.GetValue("Security:CookieSecure", !builder.Environment.IsDevelopment());
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies(options =>
{
    options.ApplicationCookie?.Configure(cookie =>
    {
        cookie.Cookie.Name = ".TravelControl.Auth";
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        cookie.Cookie.SameSite = SameSiteMode.Strict;
        cookie.SlidingExpiration = true;
        cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
        cookie.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        cookie.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("CanEdit", p => p.RequireRole(nameof(UserRole.Administrator), nameof(UserRole.Editor)))
    .AddPolicy("AdminOnly", p => p.RequireRole(nameof(UserRole.Administrator)));
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = ".TravelControl.Xsrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddRateLimiter(options =>
{
    var authPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:AuthPermitLimit") ?? 8;
    var publicPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:PublicPermitLimit") ?? 120;
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = authPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("public-read", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = publicPermitLimit, Window = TimeSpan.FromMinutes(5), QueueLimit = 0, AutoReplenishment = true }));
});
builder.Services.Configure<PublicReadOptions>(builder.Configuration.GetSection("PublicRead"));
builder.Services.AddScoped<ExcelImportService>();
builder.Services.AddScoped<IdentificationImportService>();
builder.Services.AddScoped<PassengerTravelManifestImportService>();
builder.Services.AddScoped<ExcelExportService>();
builder.Services.AddScoped<EvidenceResolver>();
builder.Services.AddScoped<PassengerQueryService>();
builder.Services.AddScoped<TripReadinessService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<PublicReadService>();
builder.Services.AddScoped<AttachmentStorage>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 10 * 1024 * 1024);

var app = builder.Build();
var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, ForwardLimit = 1 };
forwarded.KnownIPNetworks.Clear();
forwarded.KnownProxies.Clear();
app.UseForwardedHeaders(forwarded);
if (app.Environment.IsEnvironment("Testing"))
    app.UseExceptionHandler(error => error.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        await Results.Problem(exception?.ToString()).ExecuteAsync(context);
    }));
else app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; connect-src 'self'; font-src 'self' data:; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        ctx.Response.Headers["Cache-Control"] = "no-store";
        ctx.Response.Headers["Pragma"] = "no-cache";
    }
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(ctx.Request.Method)
        && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method))
    {
        try
        {
            await ctx.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(ctx);
        }
        catch (AntiforgeryValidationException)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(new { message = "La validación de seguridad venció o no es válida. Recargá la página." });
            return;
        }
    }
    await next();
});

if (app.Environment.IsDevelopment()) { app.MapOpenApi(); app.UseSwagger(); app.UseSwaggerUI(); }
app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (AppDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
app.MapGet("/robots.txt", () => Results.Text("User-agent: *\nDisallow: /\n", "text/plain"));
app.MapTravelControlApi();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

await DatabaseSeeder.InitializeAsync(app.Services);

var importIndex = Array.IndexOf(args, "--import");
if (importIndex >= 0 && args.Length > importIndex + 1)
{
    await using var scope = app.Services.CreateAsyncScope();
    var path = Path.GetFullPath(args[importIndex + 1]);
    await using var file = File.OpenRead(path);
    var summary = await scope.ServiceProvider.GetRequiredService<ExcelImportService>()
        .ProcessAsync(file, Path.GetFileName(path), args.Contains("--dry-run"), null, CancellationToken.None);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(summary, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}

await app.RunAsync();

public partial class Program;
