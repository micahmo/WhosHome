namespace WhosHome.Server.Configuration;

public class WhosHomeOptions
{
    public const string SectionName = "WhosHome";

    public double HomeLatitude { get; set; }

    public double HomeLongitude { get; set; }

    /// <summary>Inside this radius a person reads as home. Traccar Client's default
    /// distance filter is 75 m, so anything below about 150 m will flap.</summary>
    public double HomeRadiusMeters { get; set; } = 150;

    /// <summary>Inside this radius a person reads as nearby rather than away.</summary>
    public double NearbyRadiusMeters { get; set; } = 3000;

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
    /// when this is unset, so a misconfigured deployment fails closed.</summary>
    public string? AdminToken { get; set; }
}
