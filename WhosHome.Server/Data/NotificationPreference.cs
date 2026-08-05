namespace WhosHome.Server.Data;

/// <summary>
/// One person's choice about being notified for one other person. Rows exist only where someone
/// has deviated from the default, which is to hear about everyone except yourself. That makes
/// self-notification an ordinary toggle rather than a special case, so it can be switched on for
/// testing and left off in a real household without a config change.
/// </summary>
public class NotificationPreference
{
    public int Id { get; set; }

    /// <summary>The person who would receive the notification.</summary>
    public int SubscriberPersonId { get; set; }

    public Person? Subscriber { get; set; }

    /// <summary>The person the notification would be about.</summary>
    public int SubjectPersonId { get; set; }

    public Person? Subject { get; set; }

    public bool Enabled { get; set; }
}
