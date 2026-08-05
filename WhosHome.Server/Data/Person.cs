namespace WhosHome.Server.Data;

/// <summary>
/// A member of the household. <see cref="DeviceId"/> doubles as the ingest credential,
/// so it must be long and random rather than a name.
/// </summary>
public class Person
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string DeviceId { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The most recent raw fix, overwritten on every report. This is the only place raw
    /// coordinates exist, so the database cannot accumulate a location history no matter how
    /// long it runs. Kept because routing needs an origin to compute travel time from.
    /// </summary>
    public double? LastLatitude { get; set; }

    public double? LastLongitude { get; set; }

    public List<PositionReport> Reports { get; set; } = [];
}
