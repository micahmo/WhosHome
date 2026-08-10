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
        bool? isCharging,
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
        bool isMoving = IsMoving(effectiveSpeed, movedMeters, accuracyMeters);

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
            IsCharging = isCharging,
        };

        context.Reports.Add(report);
        await context.SaveChangesAsync(cancellationToken);

        return new RecordedReport(distanceMeters, previousState, currentState);
    }

    /// <summary>
    /// Records a check-in: the client saying it is alive and, because it only sends these once its
    /// own stop detection has fired, that it has stopped. With stop detection enabled a stationary
    /// phone goes quiet, so these are the only thing separating a parked phone from a lost one.
    /// No report row is written, because nothing about the position has changed.
    /// </summary>
    public async Task RecordCheckInAsync(Person person, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(person);

        person.LastCheckInUtc = timeProvider.GetUtcNow();
        await RecordContactAsync(person, cancellationToken);
    }

    /// <summary>
    /// Records contact and nothing more, for a fix too imprecise to place anyone. It proves the
    /// device is alive, which is all a check-in proves too, but unlike a check-in it says nothing
    /// about whether the device has stopped: a phone can answer with a cell-tower estimate at any
    /// point in a journey.
    /// </summary>
    public async Task RecordContactAsync(Person person, CancellationToken cancellationToken)
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

        // Movement expires, because it describes this moment and is read off a report that may be
        // old. Measured from the report rather than from last contact, since a check-in carries no
        // position and so says nothing about whether anyone is still going anywhere.
        bool movingWhenLastSeen = IsMoving(
            latest.SpeedMetersPerSecond,
            latest.MovedMeters,
            latest.AccuracyMeters);

        // The client checks in once its own stop detection has fired, so one arriving well after the
        // last position is the device saying it has parked since. That is the missing evidence
        // wherever the final fix of a journey was taken while still rolling, which with stop
        // detection is most of them: the next position may not arrive until they leave again, so
        // without this an ordinary day at work has no dwell on it at all.
        //
        // "Well after" rather than merely after, because a waking iPhone sends a heartbeat and a
        // position in the same breath, and the heartbeat can land second. One did so while its
        // owner drove at 9.26 m/s, and reading it as a stop took "On the move" off a card that had
        // just earned it. Requiring the gap to exceed the movement claim's own lifetime says what
        // is actually meant: a check-in speaks for the time after we stopped believing the fix,
        // never against a fix we still believe.
        bool checkedInSinceLastFix =
            person.LastCheckInUtc > latest.ReceivedUtc + _options.MovingClaimLifetime;

        bool isMoving = movingWhenLastSeen
            && !checkedInSinceLastFix
            && now - latest.ReceivedUtc <= _options.MovingClaimLifetime;

        // A stop is not worth describing until it has held. The anchor resets all through a journey,
        // so mid-drive the clock has always only just started, and one slow fix is enough to present
        // that as a stop rather than as the traffic light it usually is. Waiting also means every
        // duration the board shows is one worth reading.
        bool dwellHasHeld = person.StationarySinceUtc is not null
            && now - person.StationarySinceUtc.Value >= _options.DwellDebounce;

        // Gated on what the last report said rather than on isMoving, which also goes false when a
        // movement claim simply ages out. Those are different facts: one is a device saying it has
        // stopped, the other is us running out of evidence. Only the first is grounds for saying
        // anyone stopped, or for dating it. A phone with no signal keeps taking fixes and queues
        // them, so from here it is indistinguishable from a parked car, and the anchor it would be
        // dated from is the last time they travelled rather than the moment they stopped.
        DateTimeOffset? dwellSince = (movingWhenLastSeen && !checkedInSinceLastFix) || !dwellHasHeld
            ? null
            : person.StationarySinceUtc;

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
            StationarySeconds = dwellSince is null ? null : (now - dwellSince.Value).TotalSeconds,
            StationarySinceUtc = dwellSince,
            LastSeenUtc = person.LastSeenUtc ?? latest.ReceivedUtc,
            AgeSeconds = age.TotalSeconds,
            IsStale = age > _options.StaleAfter,
            BatteryPercent = latest.BatteryPercent,
            IsCharging = latest.IsCharging,
        };
    }

    /// <summary>
    /// Speed decides this, because distance between fixes cannot: a car reporting every five seconds
    /// moves under a hundred metres per fix, which is less than the noise floor a stationary phone
    /// produces over five minutes. Raw distance survives only for the first fix after a gap, where
    /// there is no interval to divide by.
    /// <para>
    /// Distance does get one job: vetoing a speed its own position contradicts.
    /// </para>
    /// </summary>
    private bool IsMoving(double? speedMetersPerSecond, double? movedMeters, double? accuracyMeters)
    {
        // A fix that went no further than its own margin of error has not established that anything
        // moved, so nothing it claims about speed is worth acting on. The platform's speed field has
        // been wrong in both directions: 0.01 m/s from a phone accelerating away from the house, and
        // 21.89 m/s from one parked overnight that had moved a single metre on a 3 m fix. Only the
        // second is caught here, and only because the position contradicted it outright.
        //
        // Real travel never trips this. Driving steps are 75 to 90 m against 2 to 10 m of accuracy,
        // two orders of magnitude clear of the test.
        if (movedMeters is not null && accuracyMeters is not null && movedMeters < accuracyMeters)
        {
            return false;
        }

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
