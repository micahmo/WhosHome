namespace WhosHome.Server.Presence;

/// <summary>
/// The outcome of recording a report. The two states are what tell a notification apart from
/// noise: someone sitting at home produces Home to Home over and over, and only a change means
/// anything happened.
/// </summary>
public sealed record RecordedReport(
    double DistanceMeters,
    PresenceState PreviousState,
    PresenceState CurrentState);
