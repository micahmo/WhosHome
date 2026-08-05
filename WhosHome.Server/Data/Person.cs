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

    public List<PositionReport> Reports { get; set; } = [];

    public List<DeviceSubscription> Subscriptions { get; set; } = [];
}
