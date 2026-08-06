using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Lib.Net.Http.WebPush;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using WhosHome.Server.Auth;
using WhosHome.Server.Configuration;
using WhosHome.Server.Data;
using WhosHome.Server.Ingest;
using WhosHome.Server.Logging;
using WhosHome.Server.Notifications;
using WhosHome.Server.Presence;
using WhosHome.Server.Retention;
using WhosHome.Server.Routing;

const string SignInPolicy = "sign-in";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Container templates hand over every field they know about, so an optional setting left blank
// arrives as an empty string. Binding "" to a double throws and the app never starts, which means
// clearing a field in the Unraid UI would break the deployment. Blank values are dropped entirely
// rather than nulled, because a null still binds as default(double) and would silently overwrite
// the intended default with zero.
Dictionary<string, string?> providedSettings = builder.Configuration
    .GetSection(WhosHomeOptions.SectionName)
    .AsEnumerable(makePathsRelative: true)
    .Where(setting => !string.IsNullOrEmpty(setting.Value))
    .ToDictionary(setting => setting.Key, setting => setting.Value);

IConfigurationRoot settings = new ConfigurationBuilder()
    .AddInMemoryCollection(providedSettings)
    .Build();

builder.Services.Configure<WhosHomeOptions>(settings);
builder.Services.AddSingleton(TimeProvider.System);

// One line per entry, timestamped, no namespaces. These logs are read through `docker logs`, where
// the default formatter's two-line entries and fully qualified categories crowd out the message.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(console => console.FormatterName = CompactConsoleFormatter.FormatterName);
builder.Logging.AddConsoleFormatter<CompactConsoleFormatter, ConsoleFormatterOptions>();

// The web client reads states by name. Integers would make the UI depend on enum ordering.
builder.Services.ConfigureHttpJsonOptions(jsonOptions =>
    jsonOptions.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

WhosHomeOptions startupOptions = settings.Get<WhosHomeOptions>() ?? new WhosHomeOptions();

string databasePath = Path.GetFullPath(startupOptions.DatabasePath);
string databaseDirectory = Path.GetDirectoryName(databasePath) ?? ".";
Directory.CreateDirectory(databaseDirectory);

builder.Services.AddDbContext<WhosHomeContext>(dbContextOptions =>
    dbContextOptions.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddScoped<PresenceService>();
builder.Services.AddHostedService<RetentionService>();

// Web push needs a stable VAPID keypair. Generated on first run and kept on the volume, because
// changing it silently invalidates every existing subscription.
// Built before the host, so it needs the formatter wired up separately or this one entry would
// arrive in a different shape from every other.
using (ILoggerFactory startupLoggerFactory = LoggerFactory.Create(logging =>
{
    logging.AddConsole(console => console.FormatterName = CompactConsoleFormatter.FormatterName);
    logging.AddConsoleFormatter<CompactConsoleFormatter, ConsoleFormatterOptions>();
}))
{
    VapidKeys vapidKeys = VapidKeyStore.LoadOrCreate(
        databaseDirectory,
        startupLoggerFactory.CreateLogger(nameof(VapidKeyStore)));
    builder.Services.AddSingleton(vapidKeys);
}

builder.Services.AddHttpClient<PushServiceClient>();
builder.Services.AddScoped<PresenceNotifier>();

// Timeout on the client itself, because this call sits inline with an incoming position report.
// The circuit is a singleton so one failure spares every subsequent report the same dead wait.
builder.Services.AddSingleton<OsrmCircuit>();
builder.Services.AddHttpClient<OsrmClient>(client => client.Timeout = startupOptions.OsrmTimeout);

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

// Deliberately loud. Without a home location every distance is measured from a point in the Gulf
// of Guinea, so everyone reads as impossibly far away and the app looks broken rather than
// unconfigured. There is no sensible default, so the only options are to complain or to mislead.
if (startupOptions.HomeLatitude == 0 && startupOptions.HomeLongitude == 0)
{
    app.Logger.LogWarning(
        "No home location configured. Set WhosHome__HomeLatitude and WhosHome__HomeLongitude, "
        + "or every person will read as thousands of miles from home.");
}

app.Logger.LogInformation(
    "Home radius {Home:F0} m, nearby radius {Nearby:F0} m, stale after {Stale}, routing {Routing}.",
    startupOptions.HomeRadiusMeters,
    startupOptions.NearbyRadiusMeters,
    startupOptions.StaleAfter,
    string.IsNullOrWhiteSpace(startupOptions.OsrmBaseUrl) ? "disabled" : startupOptions.OsrmBaseUrl);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Traccar Client speaks the OsmAnd protocol: values arrive in the query string or a form
// body, and only id, lat and lon are mandatory. The device id is the only credential, which
// is why it is long and random and why this endpoint is not behind a session.
app.MapMethods("/ingest", ["GET", "POST"], async (
    HttpRequest request,
    WhosHomeContext context,
    PresenceService presence,
    PresenceNotifier notifier,
    IOptions<WhosHomeOptions> options,
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

    if (person is null)
    {
        // 400 rather than 404 so a buffering client discards the report instead of
        // retrying an id that will never be valid.
        logger.LogWarning("Rejected report for unknown device id.");
        return Results.BadRequest(new { error = "Unknown device." });
    }

    // A heartbeat carries no position: the device is alive and has not moved. Recording it as
    // contact is what stops a stationary phone from looking like a lost one.
    if (report!.IsHeartbeat)
    {
        await presence.RecordHeartbeatAsync(person, cancellationToken);

        // Logged at information alongside positions, not at debug. Whether heartbeats arrive is
        // the one thing that cannot be reconstructed afterwards: they write no report row, and
        // the next position overwrites the contact time that would have shown one landed.
        logger.LogInformation("Heartbeat from {Name}.", person.Name);
        return Results.Ok();
    }

    // A fix too imprecise to place anyone is worth exactly what a heartbeat is worth, and is
    // treated as one. A phone waking indoors answers with a cell-tower estimate accurate to a
    // kilometre or worse; believing it announces arrivals and departures that never happened.
    WhosHomeOptions ingestOptions = options.Value;
    if (report.AccuracyMeters > ingestOptions.MaxAccuracyMeters)
    {
        await presence.RecordHeartbeatAsync(person, cancellationToken);
        logger.LogInformation(
            "Ignored an imprecise fix from {Name}: accurate to {Accuracy:F0} m against a {Limit:F0} m limit.",
            person.Name,
            report.AccuracyMeters,
            ingestOptions.MaxAccuracyMeters);
        return Results.Ok();
    }

    RecordedReport recorded = await presence.RecordAsync(
        person,
        report.Latitude!.Value,
        report.Longitude!.Value,
        report.Timestamp,
        report.AccuracyMeters,
        report.BatteryPercent,
        report.SpeedMetersPerSecond,
        report.IsCharging,
        cancellationToken);

    logger.LogInformation(
        "Report from {Name}: {Distance:F0} m from home, {Previous} to {Current}, device clock {Reported:o}.",
        person.Name,
        recorded.DistanceMeters,
        recorded.PreviousState,
        recorded.CurrentState,
        report.Timestamp);

    await notifier.NotifyAsync(
        person,
        recorded.PreviousState,
        recorded.CurrentState,
        cancellationToken);

    return Results.Ok();
});

// ---- Notifications ----

// The browser needs the public half of the VAPID pair to create a subscription. It is public by
// design and identifies this server to the push service.
app.MapGet("/api/push/key", (VapidKeys keys) => Results.Ok(new { publicKey = keys.PublicKey }));

app.MapPost("/api/push/subscribe", async (
    ClaimsPrincipal user,
    SubscribeRequest body,
    WhosHomeContext context,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(body.Endpoint)
        || string.IsNullOrWhiteSpace(body.P256dh)
        || string.IsNullOrWhiteSpace(body.Auth))
    {
        return Results.BadRequest(new { error = "Incomplete subscription." });
    }

    int personId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    DeviceSubscription? existing = await context.Subscriptions
        .FirstOrDefaultAsync(subscription => subscription.Endpoint == body.Endpoint, cancellationToken);

    if (existing is not null)
    {
        // Browsers can hand back the same endpoint with rotated keys, and the person may have
        // changed if a device was passed on, so refresh rather than duplicate.
        existing.PersonId = personId;
        existing.P256dh = body.P256dh;
        existing.Auth = body.Auth;
    }
    else
    {
        context.Subscriptions.Add(new DeviceSubscription
        {
            PersonId = personId,
            Endpoint = body.Endpoint,
            P256dh = body.P256dh,
            Auth = body.Auth,
            CreatedUtc = timeProvider.GetUtcNow(),
        });
    }

    await context.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
}).RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.Member });

// [FromBody] is required rather than inferred: minimal APIs refuse to infer a body on DELETE.
app.MapDelete("/api/push/subscribe", async (
    [FromBody] UnsubscribeRequest body,
    WhosHomeContext context,
    CancellationToken cancellationToken) =>
{
    if (!string.IsNullOrWhiteSpace(body.Endpoint))
    {
        await context.Subscriptions
            .Where(subscription => subscription.Endpoint == body.Endpoint)
            .ExecuteDeleteAsync(cancellationToken);
    }

    return Results.NoContent();
}).RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.Member });

// Who this person hears about. Returns everyone with their current setting so the UI can render
// a row per person without having to know the default rule.
app.MapGet("/api/notifications", async (
    ClaimsPrincipal user,
    WhosHomeContext context,
    CancellationToken cancellationToken) =>
{
    int personId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    Dictionary<int, bool> preferences = await context.NotificationPreferences
        .AsNoTracking()
        .Where(preference => preference.SubscriberPersonId == personId)
        .ToDictionaryAsync(
            preference => preference.SubjectPersonId,
            preference => preference.Enabled,
            cancellationToken);

    List<Person> people = await context.People
        .AsNoTracking()
        .OrderBy(person => person.SortOrder)
        .ThenBy(person => person.Id)
        .ToListAsync(cancellationToken);

    return Results.Ok(people.Select(person => new
    {
        personId = person.Id,
        name = person.Name,
        isSelf = person.Id == personId,
        enabled = preferences.TryGetValue(person.Id, out bool enabled)
            ? enabled
            : person.Id != personId,
    }));
}).RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.Member });

app.MapPut("/api/notifications/{subjectId:int}", async (
    int subjectId,
    ClaimsPrincipal user,
    NotificationPreferenceRequest body,
    WhosHomeContext context,
    CancellationToken cancellationToken) =>
{
    int personId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    if (!await context.People.AnyAsync(person => person.Id == subjectId, cancellationToken))
    {
        return Results.NotFound();
    }

    NotificationPreference? preference = await context.NotificationPreferences
        .FirstOrDefaultAsync(
            candidate => candidate.SubscriberPersonId == personId && candidate.SubjectPersonId == subjectId,
            cancellationToken);

    if (preference is null)
    {
        context.NotificationPreferences.Add(new NotificationPreference
        {
            SubscriberPersonId = personId,
            SubjectPersonId = subjectId,
            Enabled = body.Enabled,
        });
    }
    else
    {
        preference.Enabled = body.Enabled;
    }

    await context.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
}).RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.Member });

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
            candidate => candidate.LoginCode == code,
            cancellationToken);

    if (person is null || person.LoginCodeExpiresUtc is null || person.LoginCodeExpiresUtc < now)
    {
        return Results.Unauthorized();
    }

    // Single use. A code that stays valid after being used is a code that gets shared.
    //
    // Codes are typed rather than delivered as a magic link on purpose. An installed iOS web app
    // gets a storage container separate from Safari, so a link tapped in Mail would authenticate
    // the browser and leave the home screen icon signed out, with the code already spent.
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

// A question, not a protected resource, so "no" is a 200 rather than a 401. Anyone may ask whether
// this browser is an admin, and answering with 401 put a failure in everyone's network log for the
// ordinary case of not being one. The endpoints that actually do anything still refuse.
app.MapGet("/api/admin/session", async (HttpContext httpContext, IOptions<WhosHomeOptions> options) =>
{
    bool admin = await AdminAccess.IsAdminAsync(httpContext, options.Value);
    return Results.Ok(new { admin });
});

app.MapDelete("/api/admin/session", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(AuthSchemes.Admin);
    return Results.NoContent();
});

// ---- The board ----

// Readable by members and by admins. Withholding a read-only board from an admin prevents
// nothing, since an admin can mint a sign-in code for any person and become them in seconds.
app.MapGet("/api/presence", async (
    HttpContext httpContext,
    PresenceService presence,
    IOptions<WhosHomeOptions> options,
    CancellationToken cancellationToken) =>
{
    AuthenticateResult member = await httpContext.AuthenticateAsync(AuthSchemes.Member);
    if (!member.Succeeded && !await AdminAccess.IsAdminAsync(httpContext, options.Value))
    {
        return Results.Unauthorized();
    }

    IReadOnlyList<PresenceView> views = await presence.GetPresenceAsync(cancellationToken);
    return Results.Ok(views);
});

// ---- Household management, admin only ----

app.MapGet("/api/people", async (
    HttpContext httpContext,
    WhosHomeContext context,
    IOptions<WhosHomeOptions> options,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
    if (!await AdminAccess.IsAdminAsync(httpContext, options.Value))
    {
        return Results.Unauthorized();
    }

    DateTimeOffset now = timeProvider.GetUtcNow();
    string origin = PublicOrigin(httpContext.Request);

    List<Person> people = await context.People
        .AsNoTracking()
        .OrderBy(person => person.SortOrder)
        .ThenBy(person => person.Id)
        .ToListAsync(cancellationToken);

    // The live setup link comes back with each person so the admin page can show it again after
    // a refresh. Without this the only way to see a code again is to mint a new one, which
    // silently invalidates the link that was already sent to someone.
    return Results.Ok(people.Select(person =>
    {
        bool linkIsLive = person.SetupToken is not null
            && person.SetupTokenExpiresUtc is not null
            && person.SetupTokenExpiresUtc > now;

        return new
        {
            person.Id,
            person.Name,
            person.DeviceId,
            Code = linkIsLive ? person.LoginCode : null,
            SetupUrl = linkIsLive ? $"{origin}/setup/{person.SetupToken}" : null,
            ExpiresUtc = linkIsLive ? person.SetupTokenExpiresUtc : null,
        };
    }));
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

    // Appended, not inserted. Someone added while you are looking at the list should turn up at
    // the bottom where you are already looking, rather than jumping into the middle.
    int lastPosition = await context.People
        .Select(existing => (int?)existing.SortOrder)
        .MaxAsync(cancellationToken) ?? -1;

    Person person = new()
    {
        Name = body.Name.Trim(),
        DeviceId = GenerateDeviceId(),
        CreatedUtc = timeProvider.GetUtcNow(),
        SortOrder = lastPosition + 1,
    };

    context.People.Add(person);
    await context.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/people/{person.Id}", new { person.Id, person.Name, person.DeviceId });
});

app.MapPut("/api/people/order", async (
    HttpContext httpContext,
    ReorderPeopleRequest body,
    WhosHomeContext context,
    IOptions<WhosHomeOptions> options,
    CancellationToken cancellationToken) =>
{
    if (!await AdminAccess.IsAdminAsync(httpContext, options.Value))
    {
        return Results.Unauthorized();
    }

    List<Person> people = await context.People.ToListAsync(cancellationToken);

    // The whole order arrives at once, so reject anything that is not a permutation of the
    // household. A partial list would silently leave the people it omits sharing positions with
    // the ones it names, and the result would depend on the Id tiebreak rather than on intent.
    int[] requested = body.Ids ?? [];
    if (requested.Length != people.Count || requested.Distinct().Count() != requested.Length
        || !requested.OrderBy(id => id).SequenceEqual(people.Select(person => person.Id).OrderBy(id => id)))
    {
        return Results.BadRequest(new { error = "The order must list every person exactly once." });
    }

    for (int position = 0; position < requested.Length; position++)
    {
        people.Single(person => person.Id == requested[position]).SortOrder = position;
    }

    await context.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
});

app.MapDelete("/api/people/{id:int}", async (
    int id,
    HttpContext httpContext,
    WhosHomeContext context,
    IOptions<WhosHomeOptions> options,
    CancellationToken cancellationToken) =>
{
    if (!await AdminAccess.IsAdminAsync(httpContext, options.Value))
    {
        return Results.Unauthorized();
    }

    Person? person = await context.People.FindAsync([id], cancellationToken);
    if (person is null)
    {
        return Results.NotFound();
    }

    // Reports cascade. Removing someone should leave nothing of them behind, which is the
    // same promise the retention rules make.
    context.People.Remove(person);
    await context.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
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

// Lets a household member re-apply the current recommended settings to their own phone, without an
// admin minting anything. Two things make that worth having: the settings we recommend change as we
// learn what each platform actually does, and an app update can switch tracking off silently, which
// otherwise needs an admin to fix. The person comes from the session rather than the request, so
// there is no shape of this call that returns somebody else's device id.
app.MapGet("/api/device/config", async (
    ClaimsPrincipal user,
    HttpContext httpContext,
    WhosHomeContext context,
    IOptions<WhosHomeOptions> options,
    CancellationToken cancellationToken) =>
{
    int personId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    Person? person = await context.People
        .AsNoTracking()
        .FirstOrDefaultAsync(candidate => candidate.Id == personId, cancellationToken);

    if (person is null)
    {
        // The session outlived the person, so it is no longer a session for anybody.
        await httpContext.SignOutAsync(AuthSchemes.Member);
        return Results.Unauthorized();
    }

    string ingestUrl = $"{PublicOrigin(httpContext.Request)}/ingest";

    return Results.Ok(new
    {
        name = person.Name,
        traccarUrl = TraccarConfigLink.Configure(
            person,
            options.Value,
            ingestUrl,
            httpContext.Request.Headers.UserAgent.ToString()),
        startUrl = TraccarConfigLink.Start(),
        ingestUrl,
        // So the page can show whether the phone is actually being heard from, which is the only
        // feedback available: nothing here can read what settings the app currently holds.
        lastSeenUtc = person.LastSeenUtc,
    });
}).RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.Member });

// Unauthenticated by necessity: the whole point is that someone can open this before they
// have a session. The token is long, single-purpose and expires with the code.
app.MapGet("/api/setup/{token}", async (
    string token,
    HttpContext httpContext,
    WhosHomeContext context,
    IOptions<WhosHomeOptions> options,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
    WhosHomeOptions current = options.Value;
    DateTimeOffset now = timeProvider.GetUtcNow();

    Person? person = await context.People
        .AsNoTracking()
        .FirstOrDefaultAsync(candidate => candidate.SetupToken == token, cancellationToken);

    if (person is null || person.SetupTokenExpiresUtc is null || person.SetupTokenExpiresUtc < now)
    {
        return Results.NotFound(new { error = "This setup link has expired." });
    }

    string ingestUrl = $"{PublicOrigin(httpContext.Request)}/ingest";

    // This page is opened on the phone being set up, so the request itself says which platform needs
    // configuring.
    string userAgent = httpContext.Request.Headers.UserAgent.ToString();

    return Results.Ok(new
    {
        name = person.Name,
        code = person.LoginCode,
        ingestUrl,
        traccarUrl = TraccarConfigLink.Configure(person, current, ingestUrl, userAgent),
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

/// <summary>Every person's id, in the order they should appear.</summary>
public record ReorderPeopleRequest(int[]? Ids);

public record SignInRequest(string? Code);

public record AdminSignInRequest(string? Token);

public record SubscribeRequest(string? Endpoint, string? P256dh, string? Auth);

public record UnsubscribeRequest(string? Endpoint);

public record NotificationPreferenceRequest(bool Enabled);
