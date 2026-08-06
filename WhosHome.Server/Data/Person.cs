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

    /// <summary>Where this person sits on the board and in the admin list. Seeded from the order
    /// people were added, and rewritten wholesale when the admin drags a row, so it is a position
    /// rather than a rank: the values only mean anything relative to each other.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// The most recent raw fix, overwritten on every report. This is the only place raw
    /// coordinates exist, so the database cannot accumulate a location history no matter how
    /// long it runs. Kept because routing needs an origin to compute travel time from.
    /// </summary>
    public double? LastLatitude { get; set; }

    public double? LastLongitude { get; set; }

    /// <summary>Short-lived code this person types into the web app to start a session.
    /// Cleared as soon as it is used.</summary>
    public string? LoginCode { get; set; }

    public DateTimeOffset? LoginCodeExpiresUtc { get; set; }

    /// <summary>Unguessable token for this person's setup page. The page is unauthenticated by
    /// necessity, since the whole point is that they can open it before they have a session, so
    /// the token is long and expires alongside the code it reveals.</summary>
    public string? SetupToken { get; set; }

    public DateTimeOffset? SetupTokenExpiresUtc { get; set; }

    /// <summary>The state at the previous report, kept so arrivals can be detected as a change
    /// rather than re-notified on every report while someone sits at home.</summary>
    public Presence.PresenceState LastState { get; set; } = Presence.PresenceState.Unknown;

    /// <summary>
    /// The last time the device made contact at all, whether that was a position or a heartbeat.
    /// This drives the age shown on the board and whether someone reads as stale, because with
    /// stop detection enabled a phone that has not moved sends nothing for hours quite normally.
    /// </summary>
    public DateTimeOffset? LastSeenUtc { get; set; }

    /// <summary>When this person last settled in one spot, measuring the current stop rather than
    /// the time since they were last seen.</summary>
    public DateTimeOffset? StationarySinceUtc { get; set; }

    /// <summary>
    /// The spot <see cref="StationarySinceUtc"/> refers to. The clock resets when a fix lands
    /// further than the movement threshold from here, which is the only way to notice a journey made
    /// of steps that are individually too small to count: a car reporting every few seconds moves
    /// under a hundred metres per fix and can cross a county without any single pair of fixes
    /// looking like movement.
    /// </summary>
    public double? StationaryLatitude { get; set; }

    public double? StationaryLongitude { get; set; }

    /// <summary>When the previous position arrived, as opposed to the previous contact of any kind.
    /// Needed to turn a distance between two fixes into a speed for devices that report none.</summary>
    public DateTimeOffset? LastFixUtc { get; set; }

    public List<PositionReport> Reports { get; set; } = [];

    public List<DeviceSubscription> Subscriptions { get; set; } = [];
}
