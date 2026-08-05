using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhosHome.Server.Configuration;
using WhosHome.Server.Data;
using WhosHome.Server.Routing;

namespace WhosHome.Server.Presence;

public class PresenceService(
    WhosHomeContext context,
    OsrmClient routing,
    IOptions<WhosHomeOptions> options,
    TimeProvider timeProvider)
{
    private readonly WhosHomeOptions _options = options.Value;

    /// <summary>
    /// Records a report, computing and storing the distance. The raw fix overwrites the one on
    /// the person rather than being appended anywhere, so nothing accumulates.
    /// </summary>
    public async Task<RecordedReport> RecordAsync(
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

        PresenceState previousState = person.LastState;
        PresenceState currentState = Classify(distanceMeters);
        DateTimeOffset now = timeProvider.GetUtcNow();

        // Movement is measured against the previous fix rather than against home, so someone
        // driving in a circle still reads as moving.
        double? movedMeters = person.LastLatitude is null || person.LastLongitude is null
            ? null
            : GeoMath.DistanceMeters(latitude, longitude, person.LastLatitude.Value, person.LastLongitude.Value);

        bool isMoving = movedMeters > _options.MovementThresholdMeters;
        if (isMoving || person.StationarySinceUtc is null)
        {
            // Moving restarts the clock; a first report starts it.
            person.StationarySinceUtc = now;
        }

        person.LastLatitude = latitude;
        person.LastLongitude = longitude;
        person.LastState = currentState;
        person.LastSeenUtc = now;

        // Only worth asking when they are actually somewhere else. "0 minutes away" for someone
        // sitting at home is not information, and skipping it halves the routing traffic.
        RouteEstimate? route = currentState == PresenceState.Home
            ? null
            : await routing.TryGetDriveHomeAsync(latitude, longitude, cancellationToken);

        PositionReport report = new()
        {
            PersonId = person.Id,
            ReportedUtc = reportedUtc,
            ReceivedUtc = now,
            DistanceMeters = distanceMeters,
            TravelSeconds = route?.Seconds,
            MovedMeters = movedMeters,
            AccuracyMeters = accuracyMeters,
            BatteryPercent = batteryPercent,
        };

        context.Reports.Add(report);
        await context.SaveChangesAsync(cancellationToken);

        return new RecordedReport(distanceMeters, previousState, currentState);
    }

    /// <summary>
    /// Records contact without a position. With stop detection enabled the client goes quiet while
    /// stationary, so these are the only thing separating a parked phone from a lost one. No report
    /// row is written, because nothing about the position has changed.
    /// </summary>
    public async Task RecordHeartbeatAsync(Person person, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(person);

        person.LastSeenUtc = timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PresenceView>> GetPresenceAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        List<Person> people = await context.People
            .AsNoTracking()
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
            // Possible to have heard from a device before it ever sent a position, so contact
            // still decides staleness here rather than assuming the worst.
            return new PresenceView
            {
                PersonId = person.Id,
                Name = person.Name,
                State = PresenceState.Unknown,
                IsMoving = false,
                LastSeenUtc = person.LastSeenUtc,
                AgeSeconds = person.LastSeenUtc is null ? null : (now - person.LastSeenUtc.Value).TotalSeconds,
                IsStale = person.LastSeenUtc is null || now - person.LastSeenUtc.Value > _options.StaleAfter,
            };
        }

        // Age is time since the device last made contact, not since it last moved. A stationary
        // phone heartbeats without sending a position, so measuring from the last report would call
        // someone stale for the ordinary act of sitting still.
        TimeSpan age = now - (person.LastSeenUtc ?? latest.ReceivedUtc);
        bool isMoving = latest.MovedMeters > _options.MovementThresholdMeters;

        // The last known state is always reported, however old. Discarding it would throw away
        // real information; the UI shows the age alongside.
        return new PresenceView
        {
            PersonId = person.Id,
            Name = person.Name,
            State = Classify(latest.DistanceMeters),
            DistanceMeters = latest.DistanceMeters,
            TravelSeconds = latest.TravelSeconds,
            IsMoving = isMoving,
            // Suppressed while moving, and while stale, where it would otherwise claim someone has
            // been standing still for hours when really we just stopped hearing from them.
            StationarySeconds = isMoving || person.StationarySinceUtc is null
                ? null
                : (now - person.StationarySinceUtc.Value).TotalSeconds,
            StationarySinceUtc = isMoving ? null : person.StationarySinceUtc,
            LastSeenUtc = person.LastSeenUtc ?? latest.ReceivedUtc,
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
