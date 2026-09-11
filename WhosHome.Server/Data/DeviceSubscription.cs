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

    /// <summary>When a push message for this subscription was last accepted by the push service.
    /// Paired with <see cref="LastAckUtc"/>: a push service keeps accepting messages for an endpoint
    /// whose app was uninstalled or reinstalled, so delivery success alone cannot tell a live
    /// subscription from a dead one. Recorded so a later prune can act on the gap between what we
    /// send and what a living service worker acknowledges, rather than on whether anyone opened the
    /// app, which a notifications-only user never does.</summary>
    public DateTimeOffset LastPushUtc { get; set; }

    /// <summary>When the service worker last acknowledged receiving a push. A running worker
    /// acknowledges even for someone who never opens the board, so this, unlike a board load, tracks
    /// the subscription rather than the person. See <see cref="LastPushUtc"/>.</summary>
    public DateTimeOffset LastAckUtc { get; set; }
}
