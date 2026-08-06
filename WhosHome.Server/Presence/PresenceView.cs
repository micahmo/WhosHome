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

    /// <summary>Straight-line distance from home, and the only thing <see cref="State"/> is derived
    /// from. Always present, so a card has something to show when routing does not answer.</summary>
    public double? DistanceMeters { get; init; }

    /// <summary>Driving time home, when routing produced a trustworthy answer. Null is normal and
    /// simply means the card shows distance without it.</summary>
    public double? TravelSeconds { get; init; }

    /// <summary>Driving distance home, from the same answer as <see cref="TravelSeconds"/>. Preferred
    /// for display, because "eight miles away" describes the drive rather than the straight line, but
    /// it decides nothing.</summary>
    public double? TravelMeters { get; init; }

    /// <summary>True when the latest fix showed real movement rather than GPS drift.</summary>
    public required bool IsMoving { get; init; }

    /// <summary>How long they have been in one spot. Null while moving, because "here for two
    /// minutes" is meaningless when the spot keeps changing.</summary>
    public double? StationarySeconds { get; init; }

    /// <summary>When the device last made contact, by position or heartbeat. Sent absolute so the
    /// client can show a time that agrees with the age, rather than subtracting a duration from its
    /// own clock and drifting whenever the phone and the server disagree.</summary>
    public DateTimeOffset? LastSeenUtc { get; init; }

    /// <summary>When the current stop began, for the same reason.</summary>
    public DateTimeOffset? StationarySinceUtc { get; init; }

    /// <summary>Seconds since the device last made contact. A plain number rather than a TimeSpan,
    /// because .NET's TimeSpan JSON format is a .NET-ism the browser should not have to parse.</summary>
    public double? AgeSeconds { get; init; }

    /// <summary>True when the device has not made contact within the stale window, not merely when
    /// it has not moved. A stationary phone heartbeats, so this means something is actually wrong:
    /// tracking switched off, an app update, or a reboot it did not survive.</summary>
    public required bool IsStale { get; init; }

    public double? BatteryPercent { get; init; }

    /// <summary>Whether the phone was charging when it last reported. Null when the device did not
    /// say, which is why the card shows nothing rather than assuming it was not.</summary>
    public bool? IsCharging { get; init; }
}
