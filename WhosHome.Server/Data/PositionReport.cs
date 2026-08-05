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

    public double? AccuracyMeters { get; set; }

    public double? BatteryPercent { get; set; }
}
