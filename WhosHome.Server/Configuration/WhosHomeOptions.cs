namespace WhosHome.Server.Configuration;

public class WhosHomeOptions
{
    public const string SectionName = "WhosHome";

    public double HomeLatitude { get; set; }

    public double HomeLongitude { get; set; }

    /// <summary>Inside this radius a person reads as home. Traccar Client's default
    /// distance filter is 75 m, so anything below about 150 m will flap.</summary>
    public double HomeRadiusMeters { get; set; } = 150;

    /// <summary>Inside this radius a person reads as nearby rather than away, and crossing into
    /// it is what triggers a "getting close" notification. Five miles by default, which is far
    /// enough to be a useful heads up and close enough not to fire on the daily commute.</summary>
    public double NearbyRadiusMeters { get; set; } = 8047;

    /// <summary>How long without a report before the last known state is presented as history
    /// rather than as current. The state is still shown; only its presentation changes.
    /// Traccar Client stops reporting when stationary, does not resume after a phone reboot,
    /// and can be silently disabled by an app update, so age has to be visible.</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(45);

    /// <summary>How long derived reports are kept before deletion.</summary>
    public TimeSpan ReportRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Path to the SQLite file. Must point at a mounted volume in the container.</summary>
    public string DatabasePath { get; set; } = "/data/whoshome.db";

    /// <summary>Shared secret for the admin endpoints. Admin endpoints are disabled entirely
    /// when this is unset, so a misconfigured deployment fails closed. This is also the
    /// break-glass credential: it is the only way back in when no browser holds admin mode.</summary>
    public string? AdminToken { get; set; }

    /// <summary>How long a sign-in code stays usable. Long enough to send someone a setup link
    /// at lunch and have them act on it after dinner. Codes are single use regardless.</summary>
    public TimeSpan SignInCodeLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>How long a member session lasts without use. Sliding, so anyone who opens the
    /// app within the window is renewed silently and never signs in again.</summary>
    public TimeSpan MemberSessionLifetime { get; set; } = TimeSpan.FromDays(365);

    /// <summary>How long admin mode lasts in a browser without use. Shorter than a member
    /// session because it is a privileged mode used occasionally.</summary>
    public TimeSpan AdminSessionLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Sign-in attempts allowed per client per minute. Six digits is a million
    /// combinations, which is only out of reach if attempts are capped.</summary>
    public int SignInAttemptsPerMinute { get; set; } = 10;


    /// <summary>Contact address included in the VAPID token. Push services want a way to reach
    /// whoever operates the server if it misbehaves.</summary>
    public string VapidSubject { get; set; } = "mailto:admin@localhost";

    /// <summary>Base URL of a self-hosted OSRM instance, for example http://172.18.0.74:5000.
    /// Leave unset to skip travel times entirely and show straight-line distance only.</summary>
    public string? OsrmBaseUrl { get; set; }

    /// <summary>How far OSRM may snap a coordinate to a road before the answer is treated as
    /// meaningless. OSRM returns a confident route for points far outside its extract by snapping
    /// them hundreds of kilometres, so this is the real check on whether a route is about the
    /// place we asked about.</summary>
    public double OsrmMaxSnapMeters { get; set; } = 250;

    /// <summary>How long to wait on OSRM before giving up. Short, because this runs inline with
    /// an incoming position report and travel time is only enrichment. A local OSRM answers in
    /// tens of milliseconds, so this only ever elapses when something is actually wrong.</summary>
    public TimeSpan OsrmTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How long to stop asking OSRM after it fails. Must comfortably exceed the interval
    /// between incoming reports, or the cooldown expires before the next one arrives and every
    /// report pays the timeout anyway. Traccar Client reports roughly every 90 seconds per person,
    /// so minutes rather than seconds. Recovery costs at most one cooldown of missing travel
    /// times, which nobody notices.</summary>
    public TimeSpan OsrmFailureCooldown { get; set; } = TimeSpan.FromMinutes(5);
}
