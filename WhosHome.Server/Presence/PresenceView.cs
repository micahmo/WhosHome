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

    public DateTimeOffset? LastReportedUtc { get; init; }

    /// <summary>Age of the last report at the time this view was built.</summary>
    public TimeSpan? Age { get; init; }

    /// <summary>True when the last report is older than the configured stale window. The state
    /// is still the last known one; this tells the UI to present it as history rather than as
    /// current fact, which matters because Traccar Client does not resume after a phone reboot
    /// and can be silently disabled by an app update.</summary>
    public required bool IsStale { get; init; }

    public double? BatteryPercent { get; init; }
}
