using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WhosHome.Server.Configuration;
using WhosHome.Server.Data;
using WhosHome.Server.Presence;
using WhosHome.Server.Routing;

namespace WhosHome.Tests;

/// <summary>
/// One person, one real SQLite database, and a clock the test drives. Real SQLite rather than the
/// in-memory provider because the timestamps go through a value converter, and a test that skipped
/// it would not be exercising the thing that runs.
///
/// Positions are given in metres north of home so the scenarios read as journeys rather than as
/// coordinates. Speeds are metres per second, matching what is stored; the knots conversion belongs
/// to the ingest parser and is not what these tests are about.
/// </summary>
internal sealed class PresenceHarness : IAsyncDisposable
{
    private const double HomeLatitude = 42.587555;
    private const double HomeLongitude = -71.813990;
    private const double MetresPerDegreeLatitude = 111_320;

    private readonly SqliteConnection _connection;
    private readonly WhosHomeContext _context;

    private PresenceHarness(
        SqliteConnection connection,
        WhosHomeContext context,
        PresenceService service,
        FakeTimeProvider clock,
        Person person,
        WhosHomeOptions options)
    {
        _connection = connection;
        _context = context;
        Service = service;
        Clock = clock;
        Person = person;
        Options = options;
    }

    public PresenceService Service { get; }

    public FakeTimeProvider Clock { get; }

    public Person Person { get; }

    public WhosHomeOptions Options { get; }

    public static async Task<PresenceHarness> StartAsync(Action<WhosHomeOptions>? configure = null)
    {
        SqliteConnection connection = new("Filename=:memory:");
        await connection.OpenAsync();

        DbContextOptions<WhosHomeContext> contextOptions = new DbContextOptionsBuilder<WhosHomeContext>()
            .UseSqlite(connection)
            .Options;

        WhosHomeContext context = new(contextOptions);
        await context.Database.EnsureCreatedAsync();

        WhosHomeOptions options = new()
        {
            HomeLatitude = HomeLatitude,
            HomeLongitude = HomeLongitude,
        };
        configure?.Invoke(options);

        // Fully qualified: this type has its own Options property, which otherwise wins.
        IOptions<WhosHomeOptions> wrapped = Microsoft.Extensions.Options.Options.Create(options);
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero));

        // Routing is off, so this never reaches the network. It is required only because the
        // service takes one.
        OsrmClient routing = new(
            new HttpClient(),
            new OsrmCircuit(wrapped, clock),
            wrapped,
            NullLogger<OsrmClient>.Instance);

        Person person = new()
        {
            Name = "Test",
            DeviceId = "device",
            CreatedUtc = clock.GetUtcNow(),
        };
        context.People.Add(person);
        await context.SaveChangesAsync();

        return new PresenceHarness(
            connection,
            context,
            new PresenceService(context, routing, wrapped, clock),
            clock,
            person,
            options);
    }

    /// <summary>A position report, given as a distance north of home in metres.</summary>
    public Task FixAsync(double metresFromHome, double speedMetersPerSecond, double accuracyMeters = 5)
    {
        return Service.RecordAsync(
            Person,
            HomeLatitude + (metresFromHome / MetresPerDegreeLatitude),
            HomeLongitude,
            Clock.GetUtcNow(),
            accuracyMeters,
            batteryPercent: 80,
            speedMetersPerSecond,
            isCharging: false,
            CancellationToken.None);
    }

    /// <summary>What the client sends while its own stop detection says it has stopped.</summary>
    public Task CheckInAsync()
    {
        return Service.RecordCheckInAsync(Person, CancellationToken.None);
    }

    /// <summary>A fix rejected for poor accuracy. Contact, and nothing more.</summary>
    public Task RejectedFixAsync()
    {
        return Service.RecordContactAsync(Person, CancellationToken.None);
    }

    /// <summary>Stamps a drive-home estimate onto the most recent report, standing in for a fix OSRM
    /// routed. Routing is off in these tests, so every stored report otherwise has no travel time and
    /// the carry-forward would have nothing to carry.</summary>
    public async Task RouteLastFixAsync(double travelSeconds, double travelMeters)
    {
        PositionReport latest = await _context.Reports
            .OrderByDescending(report => report.ReceivedUtc)
            .FirstAsync();
        latest.TravelSeconds = travelSeconds;
        latest.TravelMeters = travelMeters;
        await _context.SaveChangesAsync();
    }

    public void Advance(TimeSpan amount)
    {
        Clock.Advance(amount);
    }

    public async Task<PresenceView> ViewAsync()
    {
        IReadOnlyList<PresenceView> views = await Service.GetPresenceAsync(CancellationToken.None);
        return views.Single();
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
