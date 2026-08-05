namespace WhosHome.Server.Data;

/// <summary>
/// A browser that has agreed to receive notifications. One person can have several, since each
/// installed copy of the app on each device subscribes separately.
/// </summary>
public class DeviceSubscription
{
    public int Id { get; set; }

    public int PersonId { get; set; }

    public Person? Person { get; set; }

    /// <summary>The push service URL the browser gave us. Unique per browser install.</summary>
    public required string Endpoint { get; set; }

    /// <summary>Browser public key used to encrypt the payload.</summary>
    public required string P256dh { get; set; }

    /// <summary>Browser auth secret used to encrypt the payload.</summary>
    public required string Auth { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
}
