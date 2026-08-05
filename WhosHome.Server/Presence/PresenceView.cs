namespace WhosHome.Server.Presence;

/// <summary>
/// What the client is allowed to see. Deliberately carries no coordinates, so there is no
/// code path in any consumer capable of rendering a map.
/// </summary>
public sealed record PresenceView
{
    public required int PersonId { get; init; }

    public required string Name { get; init; }

    /// <summary>The last known state, however old. <see cref="PresenceState.Unknown"/> means
    /// this person has never reported at all, not that their last report is stale.</summary>
    public required PresenceState State { get; init; }

    public double? DistanceMeters { get; init; }

    /// <summary>Driving time home, when routing produced a trustworthy answer. Null is normal and
    /// simply means the card shows distance without it.</summary>
    public double? TravelSeconds { get; init; }

    /// <summary>True when the latest fix showed real movement rather than GPS drift.</summary>
    public required bool IsMoving { get; init; }

    /// <summary>How long they have been in one spot. Null while moving, because "here for two
    /// minutes" is meaningless when the spot keeps changing.</summary>
    public double? StationarySeconds { get; init; }

    public DateTimeOffset? LastReportedUtc { get; init; }

    /// <summary>When the server received the last report. Sent so the client can show an absolute
    /// time that agrees with the age, rather than subtracting a duration from its own clock and
    /// drifting whenever the phone and the server disagree.</summary>
    public DateTimeOffset? LastReceivedUtc { get; init; }

    /// <summary>When the current stop began, for the same reason.</summary>
    public DateTimeOffset? StationarySinceUtc { get; init; }

    /// <summary>Seconds since the last report. A plain number rather than a TimeSpan, because
    /// .NET's TimeSpan JSON format is a .NET-ism the browser should not have to parse.</summary>
    public double? AgeSeconds { get; init; }

    /// <summary>True when the last report is older than the configured stale window. The state
    /// is still the last known one; this tells the UI to present it as history rather than as
    /// current fact, which matters because Traccar Client does not resume after a phone reboot
    /// and can be silently disabled by an app update.</summary>
    public required bool IsStale { get; init; }

    public double? BatteryPercent { get; init; }
}
