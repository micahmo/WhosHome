namespace WhosHome.Server.Data;

/// <summary>
/// One position report from a device, reduced to the distance we actually display.
/// Deliberately holds no coordinates: the latest raw fix lives on <see cref="Data.Person"/>
/// and is overwritten each time, so this table can never become a location trail.
/// </summary>
public class PositionReport
{
    public long Id { get; set; }

    public int PersonId { get; set; }

    public Person? Person { get; set; }

    /// <summary>Timestamp reported by the device.</summary>
    public DateTimeOffset ReportedUtc { get; set; }

    /// <summary>Timestamp the server received the report. Differs from <see cref="ReportedUtc"/>
    /// when iOS throttles background networking and delivers a queued fix late.</summary>
    public DateTimeOffset ReceivedUtc { get; set; }

    public double DistanceMeters { get; set; }

    /// <summary>Driving time home according to OSRM, or null when routing is off, unreachable, or
    /// the position falls outside the routing extract. Display only: it never affects state or
    /// notifications, so losing it degrades to showing plain distance.</summary>
    public double? TravelSeconds { get; set; }

    /// <summary>Driving distance home, from the same OSRM answer as <see cref="TravelSeconds"/>.
    /// Closer to how people describe how far away they are than the straight line in
    /// <see cref="DistanceMeters"/>, but never a substitute for it: state is decided by the straight
    /// line so that a routing outage cannot move anyone in or out of the house.</summary>
    public double? TravelMeters { get; set; }

    /// <summary>How far this fix is from the previous one. Null on a first report, since there is
    /// nothing to compare against. Used to tell moving from parked.</summary>
    public double? MovedMeters { get; set; }

    /// <summary>Speed the device reported, in metres per second. Preferred over
    /// <see cref="MovedMeters"/> for telling moving from parked, because it does not depend on how
    /// far apart in time two fixes happen to be. Null when the platform did not supply one.</summary>
    public double? SpeedMetersPerSecond { get; set; }

    public double? AccuracyMeters { get; set; }

    public double? BatteryPercent { get; set; }
}
