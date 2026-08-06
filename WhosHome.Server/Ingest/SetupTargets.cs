namespace WhosHome.Server.Ingest;

/// <summary>
/// Works out which kind of phone is asking for a setup link, because the two platforms need opposite
/// settings to produce the same behaviour.
/// </summary>
public static class SetupTargets
{
    /// <summary>
    /// True for an iPhone, iPad or iPod. Sniffing a user agent is normally a poor idea, but the cost
    /// of being wrong here is low and recoverable: the deep link only functions on the device that
    /// opens it, and a fresh link fixes a phone that received the wrong one.
    /// <para>
    /// iPadOS reports itself as a Mac in desktop mode, so an iPad can be misread. That is acceptable,
    /// since an iPad is unlikely to be somebody's tracker.
    /// </para>
    /// </summary>
    public static bool IsAppleMobile(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return false;
        }

        return userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("iPod", StringComparison.OrdinalIgnoreCase);
    }
}
