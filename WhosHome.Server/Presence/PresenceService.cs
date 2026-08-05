using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhosHome.Server.Configuration;
using WhosHome.Server.Data;

namespace WhosHome.Server.Presence;

public class PresenceService(
    WhosHomeContext context,
    IOptions<WhosHomeOptions> options,
    TimeProvider timeProvider)
{
    private readonly WhosHomeOptions _options = options.Value;

    /// <summary>
    /// Records a report, computing and storing the distance. The raw fix overwrites the one on
    /// the person rather than being appended anywhere, so nothing accumulates.
    /// </summary>
    public async Task<double> RecordAsync(
        Person person,
        double latitude,
        double longitude,
        DateTimeOffset reportedUtc,
        double? accuracyMeters,
        double? batteryPercent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(person);

        double distanceMeters = GeoMath.DistanceMeters(
            latitude,
            longitude,
            _options.HomeLatitude,
            _options.HomeLongitude);

        person.LastLatitude = latitude;
        person.LastLongitude = longitude;

        PositionReport report = new()
        {
            PersonId = person.Id,
            ReportedUtc = reportedUtc,
            ReceivedUtc = timeProvider.GetUtcNow(),
            DistanceMeters = distanceMeters,
            AccuracyMeters = accuracyMeters,
            BatteryPercent = batteryPercent,
        };

        context.Reports.Add(report);
        await context.SaveChangesAsync(cancellationToken);

        return distanceMeters;
    }

    public async Task<IReadOnlyList<PresenceView>> GetPresenceAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        List<Person> people = await context.People
            .AsNoTracking()
            .Where(person => person.Enabled)
            .OrderBy(person => person.Name)
            .ToListAsync(cancellationToken);

        List<PresenceView> views = new(people.Count);

        // A household is a handful of rows, so a query per person is clearer than fighting
        // EF over a grouped "latest per person" translation.
        foreach (Person person in people)
        {
            PositionReport? latest = await context.Reports
                .AsNoTracking()
                .Where(report => report.PersonId == person.Id)
                .OrderByDescending(report => report.ReceivedUtc)
                .FirstOrDefaultAsync(cancellationToken);

            views.Add(BuildView(person, latest, now));
        }

        return views;
    }

    private PresenceView BuildView(Person person, PositionReport? latest, DateTimeOffset now)
    {
        if (latest is null)
        {
            return new PresenceView
            {
                PersonId = person.Id,
                Name = person.Name,
                State = PresenceState.Unknown,
                IsStale = true,
            };
        }

        TimeSpan age = now - latest.ReceivedUtc;

        // The last known state is always reported, however old. Discarding it would throw away
        // real information; the UI shows the age alongside and greys out stale entries.
        return new PresenceView
        {
            PersonId = person.Id,
            Name = person.Name,
            State = Classify(latest.DistanceMeters),
            DistanceMeters = latest.DistanceMeters,
            LastReportedUtc = latest.ReportedUtc,
            AgeSeconds = age.TotalSeconds,
            IsStale = age > _options.StaleAfter,
            BatteryPercent = latest.BatteryPercent,
        };
    }

    private PresenceState Classify(double distanceMeters)
    {
        if (distanceMeters <= _options.HomeRadiusMeters)
        {
            return PresenceState.Home;
        }

        if (distanceMeters <= _options.NearbyRadiusMeters)
        {
            return PresenceState.Nearby;
        }

        return PresenceState.Away;
    }
}
