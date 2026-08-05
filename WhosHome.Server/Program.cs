using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using WhosHome.Server.Configuration;
using WhosHome.Server.Data;
using WhosHome.Server.Ingest;
using WhosHome.Server.Presence;
using WhosHome.Server.Retention;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<WhosHomeOptions>(builder.Configuration.GetSection(WhosHomeOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);

// The web client reads states by name. Integers would make the UI depend on enum ordering.
builder.Services.ConfigureHttpJsonOptions(jsonOptions =>
    jsonOptions.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

WhosHomeOptions startupOptions =
    builder.Configuration.GetSection(WhosHomeOptions.SectionName).Get<WhosHomeOptions>()
    ?? new WhosHomeOptions();

string databasePath = Path.GetFullPath(startupOptions.DatabasePath);
string databaseDirectory = Path.GetDirectoryName(databasePath) ?? ".";
Directory.CreateDirectory(databaseDirectory);

builder.Services.AddDbContext<WhosHomeContext>(dbContextOptions =>
    dbContextOptions.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddScoped<PresenceService>();
builder.Services.AddHostedService<RetentionService>();

// Session cookies are signed with Data Protection keys. Left at the default they live in the
// container filesystem and vanish on every image update, silently signing the whole household
// out. Keeping them next to the database puts them on the mounted volume.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(databaseDirectory, "keys")))
    .SetApplicationName("WhosHome");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(cookieOptions =>
    {
        cookieOptions.Cookie.Name = "whoshome.session";
        cookieOptions.Cookie.HttpOnly = true;
        cookieOptions.Cookie.SameSite = SameSiteMode.Lax;
        cookieOptions.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        cookieOptions.ExpireTimeSpan = TimeSpan.FromDays(365);
        cookieOptions.SlidingExpiration = true;

        // This is an API, not a server-rendered site, so unauthenticated calls get a status
        // code rather than a redirect to a login page that does not exist.
        cookieOptions.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        cookieOptions.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

WebApplication app = builder.Build();

// Migrations rather than EnsureCreated, so schema changes can reach a database that already
// has the household's history in it.
using (IServiceScope startupScope = app.Services.CreateScope())
{
    WhosHomeContext startupContext = startupScope.ServiceProvider.GetRequiredService<WhosHomeContext>();
    await startupContext.Database.MigrateAsync();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Traccar Client speaks the OsmAnd protocol: values arrive in the query string or a form
// body, and only id, lat and lon are mandatory. The device id is the only credential, which
// is why it is long and random and why this endpoint is not behind the session cookie.
app.MapMethods("/ingest", ["GET", "POST"], async (
    HttpRequest request,
    WhosHomeContext context,
    PresenceService presence,
    TimeProvider timeProvider,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    IReadOnlyDictionary<string, string?> values = await ReadValuesAsync(request);

    if (!OsmAndParser.TryParse(values, timeProvider.GetUtcNow(), out OsmAndReport? report, out string? error))
    {
        return Results.BadRequest(new { error });
    }

    Person? person = await context.People
        .FirstOrDefaultAsync(candidate => candidate.DeviceId == report!.DeviceId, cancellationToken);

    if (person is null || !person.Enabled)
    {
        // 400 rather than 404 so a buffering client discards the report instead of
        // retrying an id that will never be valid.
        logger.LogWarning("Rejected report for unknown device id.");
        return Results.BadRequest(new { error = "Unknown device." });
    }

    double distanceMeters = await presence.RecordAsync(
        person,
        report!.Latitude,
        report.Longitude,
        report.Timestamp,
        report.AccuracyMeters,
        report.BatteryPercent,
        cancellationToken);

    logger.LogInformation(
        "Report from {Name}: {Distance:F0} m from home, device clock {Reported:o}.",
        person.Name,
        distanceMeters,
        report.Timestamp);

    return Results.Ok();
});

app.MapPost("/api/session", async (
    HttpContext httpContext,
    SignInRequest body,
    WhosHomeContext context,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
    string code = (body.Code ?? string.Empty).Trim();
    if (code.Length == 0)
    {
        return Results.Unauthorized();
    }

    DateTimeOffset now = timeProvider.GetUtcNow();

    Person? person = await context.People
        .FirstOrDefaultAsync(
            candidate => candidate.Enabled && candidate.LoginCode == code,
            cancellationToken);

    if (person is null || person.LoginCodeExpiresUtc is null || person.LoginCodeExpiresUtc < now)
    {
        return Results.Unauthorized();
    }

    // Single use. A code that stays valid after being used is a code that gets shared.
    person.LoginCode = null;
    person.LoginCodeExpiresUtc = null;
    await context.SaveChangesAsync(cancellationToken);

    ClaimsPrincipal principal = new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, person.Id.ToString()),
            new Claim(ClaimTypes.Name, person.Name),
        ],
        CookieAuthenticationDefaults.AuthenticationScheme));

    await httpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal,
        new AuthenticationProperties { IsPersistent = true });

    return Results.Ok(new { personId = person.Id, name = person.Name });
});

app.MapGet("/api/session", (ClaimsPrincipal user) => Results.Ok(new
{
    personId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
    name = user.FindFirstValue(ClaimTypes.Name),
})).RequireAuthorization();

app.MapDelete("/api/session", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
});

app.MapGet("/api/presence", async (PresenceService presence, CancellationToken cancellationToken) =>
{
    IReadOnlyList<PresenceView> views = await presence.GetPresenceAsync(cancellationToken);
    return Results.Ok(views);
}).RequireAuthorization();

app.MapGet("/api/people", (
    HttpRequest request,
    WhosHomeContext context,
    IOptions<WhosHomeOptions> options) =>
{
    if (!IsAdmin(request, options.Value))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(context.People
        .AsNoTracking()
        .OrderBy(person => person.Name)
        .Select(person => new { person.Id, person.Name, person.DeviceId, person.Enabled })
        .ToList());
});

app.MapPost("/api/people", async (
    HttpRequest request,
    CreatePersonRequest body,
    WhosHomeContext context,
    IOptions<WhosHomeOptions> options,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
    if (!IsAdmin(request, options.Value))
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(body.Name))
    {
        return Results.BadRequest(new { error = "Name is required." });
    }

    Person person = new()
    {
        Name = body.Name.Trim(),
        DeviceId = GenerateDeviceId(),
        CreatedUtc = timeProvider.GetUtcNow(),
    };

    context.People.Add(person);
    await context.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/people/{person.Id}", new { person.Id, person.Name, person.DeviceId });
});

app.MapPost("/api/people/{id:int}/code", async (
    int id,
    HttpRequest request,
    WhosHomeContext context,
    IOptions<WhosHomeOptions> options,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
    if (!IsAdmin(request, options.Value))
    {
        return Results.Unauthorized();
    }

    Person? person = await context.People.FindAsync([id], cancellationToken);
    if (person is null)
    {
        return Results.NotFound();
    }

    person.LoginCode = GenerateLoginCode();
    person.LoginCodeExpiresUtc = timeProvider.GetUtcNow() + LoginCodeLifetime;
    await context.SaveChangesAsync(cancellationToken);

    return Results.Ok(new { code = person.LoginCode, expiresUtc = person.LoginCodeExpiresUtc });
});

// Any route that is not an API call or a real file is the web app: hand back index.html and
// let the client take over. Mapped last so the endpoints above win.
app.MapFallbackToFile("index.html");

app.Run();

static string GenerateDeviceId()
{
    // Long and random, because this value is the ingest credential and the onboarding
    // page hands it out. Nobody should ever have to type it.
    return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}

static string GenerateLoginCode()
{
    return RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
}

static bool IsAdmin(HttpRequest request, WhosHomeOptions options)
{
    if (string.IsNullOrWhiteSpace(options.AdminToken))
    {
        return false;
    }

    if (!request.Headers.TryGetValue("X-WhosHome-Admin-Token", out StringValues provided))
    {
        return false;
    }

    return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(provided.ToString()),
        Encoding.UTF8.GetBytes(options.AdminToken));
}

static async Task<IReadOnlyDictionary<string, string?>> ReadValuesAsync(HttpRequest request)
{
    Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase);

    foreach (KeyValuePair<string, StringValues> pair in request.Query)
    {
        values[pair.Key] = pair.Value.ToString();
    }

    if (request.HasFormContentType)
    {
        IFormCollection form = await request.ReadFormAsync();
        foreach (KeyValuePair<string, StringValues> pair in form)
        {
            values[pair.Key] = pair.Value.ToString();
        }
    }

    return values;
}

public partial class Program
{
    /// <summary>Long enough to walk across the room and read it off a screen, short enough that
    /// a guessed six-digit code is not worth the attempt.</summary>
    private static readonly TimeSpan LoginCodeLifetime = TimeSpan.FromMinutes(15);
}

public record CreatePersonRequest(string Name);

public record SignInRequest(string? Code);
