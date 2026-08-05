using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
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
string? databaseDirectory = Path.GetDirectoryName(databasePath);
if (!string.IsNullOrEmpty(databaseDirectory))
{
    Directory.CreateDirectory(databaseDirectory);
}

builder.Services.AddDbContext<WhosHomeContext>(dbContextOptions =>
    dbContextOptions.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddScoped<PresenceService>();
builder.Services.AddHostedService<RetentionService>();

WebApplication app = builder.Build();

using (IServiceScope startupScope = app.Services.CreateScope())
{
    WhosHomeContext startupContext = startupScope.ServiceProvider.GetRequiredService<WhosHomeContext>();
    await startupContext.Database.EnsureCreatedAsync();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Traccar Client speaks the OsmAnd protocol: values arrive in the query string or a form
// body, and only id, lat and lon are mandatory. The device id is the only credential.
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

app.MapGet("/api/presence", async (PresenceService presence, CancellationToken cancellationToken) =>
{
    IReadOnlyList<PresenceView> views = await presence.GetPresenceAsync(cancellationToken);
    return Results.Ok(views);
});

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

app.Run();

static string GenerateDeviceId()
{
    // Long and random, because this value is the ingest credential and the onboarding
    // page hands it out. Nobody should ever have to type it.
    return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
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

public record CreatePersonRequest(string Name);
