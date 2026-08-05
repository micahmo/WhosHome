using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using WhosHome.Server.Auth;
using WhosHome.Server.Configuration;
using WhosHome.Server.Data;
using WhosHome.Server.Ingest;
using WhosHome.Server.Presence;
using WhosHome.Server.Retention;

const string SignInPolicy = "sign-in";

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

builder.Services.AddAuthentication(AuthSchemes.Member)
    .AddCookie(AuthSchemes.Member, cookieOptions =>
    {
        ConfigureCookie(cookieOptions, "whoshome.session", startupOptions.MemberSessionLifetime);
    })
    .AddCookie(AuthSchemes.Admin, cookieOptions =>
    {
        ConfigureCookie(cookieOptions, "whoshome.admin", startupOptions.AdminSessionLifetime);
    });

builder.Services.AddAuthorization();

// Six digits is a million combinations, which only stays out of reach if attempts are capped.
builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    rateLimiterOptions.AddPolicy(SignInPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = startupOptions.SignInAttemptsPerMinute,
                Window = TimeSpan.FromMinutes(1),
            }));
});

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
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Traccar Client speaks the OsmAnd protocol: values arrive in the query string or a form
// body, and only id, lat and lon are mandatory. The device id is the only credential, which
// is why it is long and random and why this endpoint is not behind a session.
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

// ---- Member sessions ----

app.MapPost("/api/session", async (
    HttpContext httpContext,
    SignInRequest body,
    WhosHomeContext context,
    IOptions<WhosHomeOptions> options,
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
        AuthSchemes.Member));

    await httpContext.SignInAsync(
        AuthSchemes.Member,
        principal,
        new AuthenticationProperties { IsPersistent = true });

    return Results.Ok(new { personId = person.Id, name = person.Name });
}).RequireRateLimiting(SignInPolicy);

app.MapGet("/api/session", (ClaimsPrincipal user) => Results.Ok(new
{
    personId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
    name = user.FindFirstValue(ClaimTypes.Name),
})).RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.Member });

app.MapDelete("/api/session", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(AuthSchemes.Member);
    return Results.NoContent();
});

// ---- Admin mode ----

// Admin is a role a browser enters, not a person. The machine used for provisioning never has
// to exist on the board.
app.MapPost("/api/admin/session", async (
    HttpContext httpContext,
    AdminSignInRequest body,
    IOptions<WhosHomeOptions> options,
    CancellationToken cancellationToken) =>
{
    WhosHomeOptions current = options.Value;
    if (string.IsNullOrWhiteSpace(current.AdminToken))
    {
        return Results.Unauthorized();
    }

    if (!AdminAccess.ConstantTimeEquals(body.Token?.Trim(), current.AdminToken))
    {
        return Results.Unauthorized();
    }

    ClaimsPrincipal principal = new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, "admin")],
        AuthSchemes.Admin));

    // The raw token is exchanged for a cookie and never stored in the browser.
    await httpContext.SignInAsync(
        AuthSchemes.Admin,
        principal,
        new AuthenticationProperties { IsPersistent = true });

    return Results.Ok(new { admin = true });
}).RequireRateLimiting(SignInPolicy);

app.MapGet("/api/admin/session", async (HttpContext httpContext, IOptions<WhosHomeOptions> options) =>
{
    bool admin = await AdminAccess.IsAdminAsync(httpContext, options.Value);
    return admin ? Results.Ok(new { admin = true }) : Results.Unauthorized();
});

app.MapDelete("/api/admin/session", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(AuthSchemes.Admin);
    return Results.NoContent();
});

// ---- The board ----

app.MapGet("/api/presence", async (PresenceService presence, CancellationToken cancellationToken) =>
{
    IReadOnlyList<PresenceView> views = await presence.GetPresenceAsync(cancellationToken);
    return Results.Ok(views);
}).RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.Member });

// ---- Household management, admin only ----

app.MapGet("/api/people", async (
    HttpContext httpContext,
    WhosHomeContext context,
    IOptions<WhosHomeOptions> options,
    CancellationToken cancellationToken) =>
{
    if (!await AdminAccess.IsAdminAsync(httpContext, options.Value))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(await context.People
        .AsNoTracking()
        .OrderBy(person => person.Name)
        .Select(person => new { person.Id, person.Name, person.DeviceId, person.Enabled })
        .ToListAsync(cancellationToken));
});

app.MapPost("/api/people", async (
    HttpContext httpContext,
    CreatePersonRequest body,
    WhosHomeContext context,
    IOptions<WhosHomeOptions> options,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
    if (!await AdminAccess.IsAdminAsync(httpContext, options.Value))
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
    HttpContext httpContext,
    WhosHomeContext context,
    IOptions<WhosHomeOptions> options,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
    WhosHomeOptions current = options.Value;
    if (!await AdminAccess.IsAdminAsync(httpContext, current))
    {
        return Results.Unauthorized();
    }

    Person? person = await context.People.FindAsync([id], cancellationToken);
    if (person is null)
    {
        return Results.NotFound();
    }

    DateTimeOffset expiresUtc = timeProvider.GetUtcNow() + current.SignInCodeLifetime;

    person.LoginCode = GenerateLoginCode();
    person.LoginCodeExpiresUtc = expiresUtc;

    // The setup page is the thing actually handed to a person, so it expires with the code
    // it reveals rather than lingering as a permanent URL exposing a device id.
    person.SetupToken = GenerateSetupToken();
    person.SetupTokenExpiresUtc = expiresUtc;

    await context.SaveChangesAsync(cancellationToken);

    return Results.Ok(new
    {
        code = person.LoginCode,
        expiresUtc,
        setupUrl = $"{PublicOrigin(httpContext.Request)}/setup/{person.SetupToken}",
    });
});

// Unauthenticated by necessity: the whole point is that someone can open this before they
// have a session. The token is long, single-purpose and expires with the code.
app.MapGet("/api/setup/{token}", async (
    string token,
    HttpContext httpContext,
    WhosHomeContext context,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
    DateTimeOffset now = timeProvider.GetUtcNow();

    Person? person = await context.People
        .AsNoTracking()
        .FirstOrDefaultAsync(candidate => candidate.SetupToken == token, cancellationToken);

    if (person is null || person.SetupTokenExpiresUtc is null || person.SetupTokenExpiresUtc < now)
    {
        return Results.NotFound(new { error = "This setup link has expired." });
    }

    string ingestUrl = $"{PublicOrigin(httpContext.Request)}/ingest";

    return Results.Ok(new
    {
        name = person.Name,
        code = person.LoginCode,
        ingestUrl,
        // Verified against the Traccar Client source: custom scheme, any host except "action",
        // and the parameter names are url/id, not serverUrl/deviceId as the forums claim.
        traccarUrl =
            $"org.traccar.client://configure?url={Uri.EscapeDataString(ingestUrl)}"
            + $"&id={person.DeviceId}&accuracy=medium&distance=75&interval=300&stop_detection=true",
        expiresUtc = person.SetupTokenExpiresUtc,
    });
}).RequireRateLimiting(SignInPolicy);

// An unmatched API route must 404 rather than fall through to the app shell below. Otherwise
// a typo'd endpoint quietly returns HTML and surfaces as a JSON parse error somewhere else.
app.Map("/api/{**rest}", () => Results.NotFound());

// Any route that is not an API call or a real file is the web app: hand back index.html and
// let the client take over. Mapped last so the endpoints above win.
app.MapFallbackToFile("index.html");

app.Run();

static void ConfigureCookie(CookieAuthenticationOptions cookieOptions, string name, TimeSpan lifetime)
{
    cookieOptions.Cookie.Name = name;
    cookieOptions.Cookie.HttpOnly = true;
    cookieOptions.Cookie.SameSite = SameSiteMode.Lax;
    cookieOptions.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    cookieOptions.ExpireTimeSpan = lifetime;
    cookieOptions.SlidingExpiration = true;

    // This is an API, not a server-rendered site, so unauthenticated calls get a status code
    // rather than a redirect to a login page that does not exist.
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
}

static string ClientKey(HttpContext context)
{
    // Behind the Cloudflare tunnel every request arrives from the cloudflared container, so
    // partitioning on the socket address would put the whole household in one bucket. The
    // app is only reachable through the tunnel, so this header is trustworthy here.
    if (context.Request.Headers.TryGetValue("CF-Connecting-IP", out StringValues forwarded)
        && !StringValues.IsNullOrEmpty(forwarded))
    {
        return forwarded.ToString();
    }

    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

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

static string GenerateSetupToken()
{
    return Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
}

static string PublicOrigin(HttpRequest request)
{
    // The tunnel terminates TLS and forwards plain HTTP, so request.Scheme says "http" even
    // though the browser used HTTPS. Without this the setup links we hand out would be
    // http:// and would not work.
    string scheme = request.Headers.TryGetValue("X-Forwarded-Proto", out StringValues forwarded)
        && !StringValues.IsNullOrEmpty(forwarded)
            ? forwarded.ToString().Split(',')[0].Trim()
            : request.Scheme;

    return $"{scheme}://{request.Host}";
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

public record CreatePersonRequest(string Name);

public record SignInRequest(string? Code);

public record AdminSignInRequest(string? Token);
