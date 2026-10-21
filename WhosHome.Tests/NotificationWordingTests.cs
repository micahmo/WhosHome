using WhosHome.Server.Notifications;
using WhosHome.Server.Presence;

namespace WhosHome.Tests;

/// <summary>
/// The wording a notification carries. The prompt body is the friendly one; the late stem is what the
/// service worker dates when a push surfaces hours after the event, so "Just left home" becomes "Left
/// home at 8:14 AM" rather than claiming something happened just now when it happened this morning.
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
        Assert.Equal("Just left home.", announcement.Body);
        Assert.Equal("Left home", announcement.LateBody);
    }

    [Theory]
    [InlineData(PresenceState.Away, PresenceState.Home, "Just arrived.", "Arrived home")]
    [InlineData(PresenceState.Away, PresenceState.Nearby, "Getting close to home.", "Near home")]
    [InlineData(PresenceState.Nearby, PresenceState.Away, "Left the area.", "Left the area")]
    public void Every_announcement_pairs_a_prompt_body_with_a_dateable_stem(
        PresenceState previous,
        PresenceState current,
        string body,
        string lateBody)
    {
        PresenceNotifier.Announcement? announcement =
            PresenceNotifier.Describe("Sevano", previous, current);

        Assert.NotNull(announcement);
        Assert.Equal(body, announcement!.Body);
        Assert.Equal(lateBody, announcement.LateBody);
    }

    [Fact]
    public void A_first_ever_report_announces_nothing()
    {
        // No previous state to compare to, so there is no arrival or departure to describe.
        Assert.Null(PresenceNotifier.Describe("Micah", PresenceState.Unknown, PresenceState.Home));
    }
}
