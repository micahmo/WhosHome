namespace WhosHome.Server.Configuration;

public class WhosHomeOptions
{
    public const string SectionName = "WhosHome";

    public double HomeLatitude { get; set; }

    public double HomeLongitude { get; set; }

    /// <summary>Inside this radius a person reads as home. Traccar Client's default
    /// distance filter is 75 m, so anything below about 150 m will flap.</summary>
    public double HomeRadiusMeters { get; set; } = 150;

    /// <summary>Inside this radius a person reads as near home rather than away, and crossing into
    /// it is what triggers a "getting close" notification.
    /// <para>
    /// A straight line stands in for a drive here, so the number has to absorb the difference.
    /// Sampling eighteen directions around one household put two driving miles at a median of 1.47
    /// straight-line miles, a detour factor of about 1.34, so this is roughly two miles of driving
    /// rather than two miles of crow. Somewhere with a denser or sparser road network would want a
    /// different figure, which is why this is configurable and not a constant.
    /// </para></summary>
    public double NearbyRadiusMeters { get; set; } = 2400;

    /// <summary>Worst reported accuracy a fix may have and still be believed. A phone waking up
    /// indoors answers with a cell-tower estimate accurate to a kilometre or more, which cannot
    /// tell <see cref="HomeRadiusMeters"/> from the next town and has announced people leaving and
    /// arriving in the small hours. Anything worse than this counts only as proof of life.</summary>
    public double MaxAccuracyMeters { get; set; } = 250;

    /// <summary>Above this speed someone reads as on the move. Compared against the speed the
    /// device reports, not against the gap between fixes: while driving, reports arrive seconds
    /// apart, so each step is shorter than <see cref="MovementThresholdMeters"/> and distance alone
    /// calls a moving car parked. Roughly walking pace, well clear of a stationary phone's noise.</summary>
    public double MovingSpeedMetersPerSecond { get; set; } = 1.5;

    /// <summary>How long without any contact before someone reads as stale. Contact includes
    /// heartbeats, so this measures "the phone has stopped talking to us" rather than "the person
    /// has not moved". Should be a small multiple of <see cref="HeartbeatInterval"/> so a single
    /// missed check-in is tolerated.</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(45);

    /// <summary>How often the client should check in while stationary, handed to it in the setup
    /// link. Without this, stop detection means a parked phone sends nothing for hours and there is
    /// no way to distinguish that from one that has stopped working.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How far a fix must be from the previous one to count as movement rather than GPS
    /// noise. Reports arrive with around 100 m accuracy and Traccar Client's own distance filter
    /// is 75 m, so a stationary phone can appear to wander over 100 m. This sits above that.</summary>
    public double MovementThresholdMeters { get; set; } = 200;

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
