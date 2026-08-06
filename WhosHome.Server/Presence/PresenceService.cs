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
        double? speedMetersPerSecond,
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

        // The device's own figure when it offers one, since it comes from the GPS rather than from
        // arithmetic. iOS supplies it consistently and Android only on some fixes, so the fallback
        // has to exist. It is also the safer of the two during a buffered upload: a phone flushing a
        // backlog delivers several positions in the same second, and distance over interval then
        // works out at thousands of metres per second.
        double? effectiveSpeed = speedMetersPerSecond ?? ComputeSpeed(movedMeters, person.LastFixUtc, now);
        bool isMoving = IsMoving(effectiveSpeed, movedMeters);

        // Measured from where the clock started, not from the previous fix. Consecutive fixes while
        // driving are only tens of metres apart, so a per-fix comparison never fires however far the
        // journey goes, and the clock would keep counting from wherever it last happened to reset.
        double? metersFromAnchor = person.StationaryLatitude is null || person.StationaryLongitude is null
            ? null
            : GeoMath.DistanceMeters(
                latitude,
                longitude,
                person.StationaryLatitude.Value,
                person.StationaryLongitude.Value);

        bool hasRelocated = metersFromAnchor > _options.MovementThresholdMeters;

        // Relocating restarts the clock; moving does not. Speed says what someone is doing this
        // instant, and a single reading is a poor reason to rewrite how long they have been
        // somewhere: a phone at rest indoors reported 2 m/s and reset a clock that had legitimately
        // been running for forty minutes. Distance from the anchor is the durable question, and
        // pacing around the house cannot answer it wrongly.
        if (person.StationarySinceUtc is null || metersFromAnchor is null || hasRelocated)
        {
            person.StationarySinceUtc = now;
            person.StationaryLatitude = latitude;
            person.StationaryLongitude = longitude;
        }

        person.LastLatitude = latitude;
        person.LastLongitude = longitude;
        person.LastState = currentState;
        person.LastSeenUtc = now;
        person.LastFixUtc = now;

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
            TravelMeters = route?.Meters,
            MovedMeters = movedMeters,
            // The effective one, so the board reads the same number this decision was made on.
            SpeedMetersPerSecond = effectiveSpeed,
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

        // Not alphabetical, so the board stays put as the household grows. SortOrder starts as the
        // order people were added and is whatever the admin has since dragged it to. Id breaks
        // ties, which only arise if two rows are somehow written the same position.
        List<Person> people = await context.People
            .AsNoTracking()
            .OrderBy(person => person.SortOrder)
            .ThenBy(person => person.Id)
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
        bool isMoving = IsMoving(latest.SpeedMetersPerSecond, latest.MovedMeters);

        // The last known state is always reported, however old. Discarding it would throw away
        // real information; the UI shows the age alongside.
        return new PresenceView
        {
            PersonId = person.Id,
            Name = person.Name,
            State = Classify(latest.DistanceMeters),
            DistanceMeters = latest.DistanceMeters,
            TravelSeconds = latest.TravelSeconds,
            TravelMeters = latest.TravelMeters,
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

    /// <summary>
    /// Speed decides this, because distance between fixes cannot: a car reporting every five seconds
    /// moves under a hundred metres per fix, which is less than the noise floor a stationary phone
    /// produces over five minutes. Raw distance survives only for the first fix after a gap, where
    /// there is no interval to divide by.
    /// </summary>
    private bool IsMoving(double? speedMetersPerSecond, double? movedMeters)
    {
        if (speedMetersPerSecond is not null)
        {
            return speedMetersPerSecond > _options.MovingSpeedMetersPerSecond;
        }

        return movedMeters > _options.MovementThresholdMeters;
    }

    /// <summary>
    /// Speed between two fixes. Null when there is nothing to measure against, or when the two
    /// arrived in the same instant, which would divide by zero and report an infinite speed.
    /// </summary>
    private static double? ComputeSpeed(double? movedMeters, DateTimeOffset? previousFixUtc, DateTimeOffset now)
    {
        if (movedMeters is null || previousFixUtc is null)
        {
            return null;
        }

        double seconds = (now - previousFixUtc.Value).TotalSeconds;
        if (seconds <= 0)
        {
            return null;
        }

        return movedMeters.Value / seconds;
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
