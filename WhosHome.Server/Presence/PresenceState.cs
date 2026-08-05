namespace WhosHome.Server.Presence;

public enum PresenceState
{
    /// <summary>No report has ever arrived, or the last one is older than the stale window.</summary>
    Unknown,

    Home,

    Nearby,

    Away,
}
