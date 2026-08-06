using WhosHome.Server.Configuration;
using WhosHome.Server.Data;

namespace WhosHome.Server.Ingest;

/// <summary>
/// Builds the deep links that configure Traccar Client. Two pages hand these out, onboarding and the
/// device page, and they must never disagree: a phone that took its settings from one and its updates
/// from the other would end up in a state neither page believes it is in.
/// </summary>
public static class TraccarConfigLink
{
    /// <summary>Any host except "action" works for configuration; "action" is reserved for the
    /// start and stop verbs below.</summary>
    private const string Scheme = "org.traccar.client://";

    /// <summary>
    /// The one-tap configuration link for a person's phone.
    /// <para>
    /// Both branches exist so that silence means something. A stationary phone that sends nothing is
    /// indistinguishable from one switched off, out of signal, or with tracking disabled by an app
    /// update, which is the exact case the staleness warning is for.
    /// </para>
    /// </summary>
    public static string Configure(Person person, WhosHomeOptions options, string ingestUrl, string? userAgent)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(options);

        int checkInSeconds = (int)options.HeartbeatInterval.TotalSeconds;

        string tracking = SetupTargets.IsAppleMobile(userAgent)
            // iOS heartbeats never fire: the client asks BGTaskScheduler for an identifier its own
            // Info.plist does not declare. Stop detection has to come off or an iPhone at rest says
            // nothing at all. The interval becomes the check-in cadence; movement still reports at
            // `distance` regardless, because the client's filters are an OR rather than an AND.
            ? $"&interval={checkInSeconds}&stop_detection=false"
            // Android heartbeats do work, observed arriving on schedule, so stop detection stays on
            // and costs nothing: the phone sleeps between check-ins instead of tracking continuously.
            : $"&interval=300&heartbeat={checkInSeconds}&stop_detection=true";

        return $"{Scheme}configure?url={Uri.EscapeDataString(ingestUrl)}"
            + $"&id={person.DeviceId}&accuracy=medium&distance=75"
            + tracking;
    }

    /// <summary>Starts tracking with no confirmation dialog. Applying settings does not start
    /// tracking, so this is a separate tap, and it is the only recovery from an app update having
    /// quietly switched tracking off.</summary>
    public static string Start() => $"{Scheme}action/start";
}
