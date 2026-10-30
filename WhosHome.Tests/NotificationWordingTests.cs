using WhosHome.Server.Notifications;
using WhosHome.Server.Presence;

namespace WhosHome.Tests;

/// <summary>
/// The wording a notification carries. The body is a stem the service worker always dates, so "Left
/// home" is shown as "Left home at 8:14 AM." and stays true however late the notification surfaces.
/// </summary>
public class NotificationWordingTests
{
    [Fact]
    public void Leaving_home_reads_as_left_however_far_they_then_got()
    {
        // Home to Away rather than Home to Nearby: the "left home" wording wins over the outer ring,
        // because the thing worth saying is that they left, not which ring they reached.
        PresenceNotifier.Announcement? announcement =
            PresenceNotifier.Describe("Alaina", PresenceState.Home, PresenceState.Away);

        Assert.NotNull(announcement);
        Assert.Equal("Alaina left", announcement!.Title);
        Assert.Equal("Left home", announcement.Body);
    }

    [Theory]
    [InlineData(PresenceState.Away, PresenceState.Home, "Arrived home")]
    [InlineData(PresenceState.Away, PresenceState.Nearby, "Near home")]
    [InlineData(PresenceState.Nearby, PresenceState.Away, "Left the area")]
    public void Every_announcement_is_a_dateable_stem(
        PresenceState previous,
        PresenceState current,
        string body)
    {
        PresenceNotifier.Announcement? announcement =
            PresenceNotifier.Describe("Sevano", previous, current);

        Assert.NotNull(announcement);
        Assert.Equal(body, announcement!.Body);
    }

    [Fact]
    public void A_first_ever_report_announces_nothing()
    {
        // No previous state to compare to, so there is no arrival or departure to describe.
        Assert.Null(PresenceNotifier.Describe("Micah", PresenceState.Unknown, PresenceState.Home));
    }
}
