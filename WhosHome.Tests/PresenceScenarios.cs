using WhosHome.Server.Presence;

namespace WhosHome.Tests;

/// <summary>
/// Every scenario here happened. They are the cases that made the presence rules what they are, so
/// the point of running them together is that a change made for one cannot quietly break another.
/// Each test names the situation and cites the reading that produced it.
/// </summary>
public class PresenceScenarios
{
    // ---- Driving ----

    [Fact]
    public async Task Driving_reads_as_moving_and_claims_no_dwell()
    {
        await using PresenceHarness harness = await PresenceHarness.StartAsync();

        await harness.FixAsync(5_000, speedMetersPerSecond: 20);
        harness.Advance(TimeSpan.FromSeconds(5));
        await harness.FixAsync(5_100, speedMetersPerSecond: 20);

        PresenceView view = await harness.ViewAsync();

        Assert.True(view.IsMoving);
        Assert.Null(view.StationarySeconds);
    }

    [Fact]
    public async Task Movement_stops_being_believed_once_the_claim_outlives_its_report()
    {
        await using PresenceHarness harness = await PresenceHarness.StartAsync();

        await harness.FixAsync(5_000, speedMetersPerSecond: 20);
        harness.Advance(harness.Options.MovingClaimLifetime + TimeSpan.FromMinutes(1));

        PresenceView view = await harness.ViewAsync();

        Assert.False(view.IsMoving);
    }

    // ---- Arriving ----

    [Fact]
    public async Task An_arrival_claims_no_duration_until_the_stop_has_held()
    {
        await using PresenceHarness harness = await PresenceHarness.StartAsync();

        await harness.FixAsync(3_000, speedMetersPerSecond: 15);
        harness.Advance(TimeSpan.FromMinutes(2));
        await harness.FixAsync(50, speedMetersPerSecond: 0);

        PresenceView view = await harness.ViewAsync();

        Assert.False(view.IsMoving);
        Assert.Null(view.StationarySeconds);
    }

    [Fact]
    public async Task An_arrival_reports_its_duration_once_the_stop_has_held()
    {
        await using PresenceHarness harness = await PresenceHarness.StartAsync();

        await harness.FixAsync(3_000, speedMetersPerSecond: 15);
        harness.Advance(TimeSpan.FromMinutes(2));
        await harness.FixAsync(50, speedMetersPerSecond: 0);
        harness.Advance(harness.Options.DwellDebounce + TimeSpan.FromMinutes(1));

        PresenceView view = await harness.ViewAsync();

        Assert.NotNull(view.StationarySeconds);
        Assert.Equal(
            (harness.Options.DwellDebounce + TimeSpan.FromMinutes(1)).TotalSeconds,
            view.StationarySeconds!.Value,
            precision: 0);
    }

    [Fact]
    public async Task A_dwell_keeps_counting_through_a_long_silence()
    {
        // Two iPhones arrived home, went quiet as iOS does, and were still correctly showing a
        // twelve hour dwell the next morning.
        await using PresenceHarness harness = await PresenceHarness.StartAsync();

        await harness.FixAsync(50, speedMetersPerSecond: 0);
        harness.Advance(TimeSpan.FromHours(12));

        PresenceView view = await harness.ViewAsync();

        Assert.True(view.IsStale);
        Assert.NotNull(view.StationarySeconds);
        Assert.Equal(TimeSpan.FromHours(12).TotalSeconds, view.StationarySeconds!.Value, precision: 0);
    }

    // ---- Fixes that lie ----

    [Fact]
    public async Task One_slow_fix_mid_journey_does_not_become_a_stop()
    {
        // A traffic light, or any single reading below the moving threshold while driving.
        await using PresenceHarness harness = await PresenceHarness.StartAsync();

        await harness.FixAsync(5_000, speedMetersPerSecond: 20);
        harness.Advance(TimeSpan.FromSeconds(30));
        await harness.FixAsync(4_500, speedMetersPerSecond: 0.5);

        PresenceView view = await harness.ViewAsync();

        Assert.False(view.IsMoving);
        Assert.Null(view.StationarySeconds);
    }

    [Fact]
    public async Task A_waking_fix_reading_zero_while_driving_does_not_become_a_stop()
    {
        // Observed: a phone waking after fourteen hours reported 0.01 m/s at 32 m accuracy while
        // its owner drove away from the house, corrected 52 seconds later by a fix reading 5.3 m/s.
        await using PresenceHarness harness = await PresenceHarness.StartAsync();

        await harness.FixAsync(50, speedMetersPerSecond: 0);
        harness.Advance(TimeSpan.FromHours(14));
        await harness.FixAsync(336, speedMetersPerSecond: 0.01, accuracyMeters: 32);

        PresenceView view = await harness.ViewAsync();

        Assert.Null(view.StationarySeconds);
    }

    [Fact]
    public async Task A_speed_the_fixs_own_position_contradicts_is_ignored()
    {
        // Observed: 21.89 m/s from a phone parked at home overnight that had moved one metre on a
        // three metre fix. Believing it erased the arrival line from a stale card.
        await using PresenceHarness harness = await PresenceHarness.StartAsync();

        await harness.FixAsync(50, speedMetersPerSecond: 0);
        harness.Advance(TimeSpan.FromHours(6));
        await harness.FixAsync(51, speedMetersPerSecond: 21.89, accuracyMeters: 3);

        PresenceView view = await harness.ViewAsync();

        Assert.False(view.IsMoving);
        Assert.NotNull(view.StationarySeconds);
    }

    [Fact]
    public async Task The_same_absurd_speed_still_counts_when_the_phone_really_moved()
    {
        // The veto must only fire on a self-contradictory fix, never on real travel.
        await using PresenceHarness harness = await PresenceHarness.StartAsync();

        await harness.FixAsync(50, speedMetersPerSecond: 0);
        harness.Advance(TimeSpan.FromMinutes(5));
        await harness.FixAsync(3_000, speedMetersPerSecond: 21.89, accuracyMeters: 5);

        PresenceView view = await harness.ViewAsync();

        Assert.True(view.IsMoving);
        Assert.Null(view.StationarySeconds);
    }

    // ---- Silence ----

    [Fact]
    public async Task A_dead_zone_after_a_moving_fix_claims_nothing_at_all()
    {
        // Observed: two phones lost signal at 26.9 km doing 19.5 m/s and kept driving for ten more
        // minutes. The board had been announcing a stop the whole time.
        await using PresenceHarness harness = await PresenceHarness.StartAsync();

        await harness.FixAsync(20_000, speedMetersPerSecond: 15);
        harness.Advance(TimeSpan.FromSeconds(30));
        await harness.FixAsync(26_900, speedMetersPerSecond: 19.5);
        harness.Advance(TimeSpan.FromMinutes(40));

        PresenceView view = await harness.ViewAsync();

        Assert.False(view.IsMoving);
        Assert.Null(view.StationarySeconds);
        Assert.Null(view.StationarySinceUtc);
    }

    [Fact]
    public async Task A_check_in_after_a_moving_fix_restores_the_dwell()
    {
        // The commonest Android day: the last fix of the commute is taken while still rolling into
        // the car park, and stop detection means no further position arrives until home time.
        await using PresenceHarness harness = await PresenceHarness.StartAsync();

        await harness.FixAsync(30_000, speedMetersPerSecond: 12);
        harness.Advance(TimeSpan.FromSeconds(20));
        await harness.FixAsync(32_794, speedMetersPerSecond: 5.59, accuracyMeters: 20);

        harness.Advance(TimeSpan.FromMinutes(20));
        await harness.CheckInAsync();

        PresenceView view = await harness.ViewAsync();

        Assert.False(view.IsMoving);
        Assert.NotNull(view.StationarySeconds);
        Assert.Equal(TimeSpan.FromMinutes(20).TotalSeconds, view.StationarySeconds!.Value, precision: 0);
    }

    [Fact]
    public async Task A_check_in_in_the_same_breath_as_a_moving_fix_proves_nothing()
    {
        // A waking iPhone sends a heartbeat and a position together. Observed: both logged in the
        // same second while its owner drove at 9.26 m/s, which cleared the movement claim and put
        // a stopped card on the board. A check-in speaks for the time after a fix stops being
        // believed, never against one still fresh.
        await using PresenceHarness harness = await PresenceHarness.StartAsync();

        await harness.FixAsync(50, speedMetersPerSecond: 0);
        harness.Advance(TimeSpan.FromMinutes(10));
        await harness.FixAsync(300, speedMetersPerSecond: 9.26, accuracyMeters: 10);

        harness.Advance(TimeSpan.FromSeconds(1));
        await harness.CheckInAsync();

        PresenceView view = await harness.ViewAsync();

        Assert.True(view.IsMoving);
        Assert.Null(view.StationarySeconds);
    }

    [Fact]
    public async Task A_fix_rejected_for_poor_accuracy_is_not_a_check_in()
    {
        // It proves the device is alive and nothing else. A phone can answer with a cell tower
        // estimate at any point in a journey, and one did so mid-drive.
        await using PresenceHarness harness = await PresenceHarness.StartAsync();

        await harness.FixAsync(20_000, speedMetersPerSecond: 15);
        harness.Advance(TimeSpan.FromSeconds(30));
        await harness.FixAsync(26_900, speedMetersPerSecond: 19.5);

        harness.Advance(TimeSpan.FromMinutes(20));
        await harness.RejectedFixAsync();

        PresenceView view = await harness.ViewAsync();

        Assert.Null(view.StationarySeconds);
        Assert.Null(view.StationarySinceUtc);
    }

    // ---- Stale cards ----

    [Fact]
    public async Task A_stale_card_keeps_its_arrival_when_the_last_fix_said_stopped()
    {
        await using PresenceHarness harness = await PresenceHarness.StartAsync();

        await harness.FixAsync(11_200, speedMetersPerSecond: 0.43);
        harness.Advance(harness.Options.StaleAfter + TimeSpan.FromHours(1));

        PresenceView view = await harness.ViewAsync();

        Assert.True(view.IsStale);
        Assert.NotNull(view.StationarySinceUtc);
    }

    [Fact]
    public async Task A_stale_card_has_no_arrival_when_the_last_fix_said_moving()
    {
        await using PresenceHarness harness = await PresenceHarness.StartAsync();

        await harness.FixAsync(11_200, speedMetersPerSecond: 15);
        harness.Advance(harness.Options.StaleAfter + TimeSpan.FromHours(1));

        PresenceView view = await harness.ViewAsync();

        Assert.True(view.IsStale);
        Assert.Null(view.StationarySinceUtc);
    }

    // ---- Staleness is about contact, not movement ----

    [Fact]
    public async Task Check_ins_keep_a_parked_phone_from_reading_as_lost()
    {
        await using PresenceHarness harness = await PresenceHarness.StartAsync();

        await harness.FixAsync(50, speedMetersPerSecond: 0);

        // Longer than the stale window in total, but never that long without contact.
        for (int checkIn = 0; checkIn < 6; checkIn++)
        {
            harness.Advance(TimeSpan.FromMinutes(20));
            await harness.CheckInAsync();
        }

        PresenceView view = await harness.ViewAsync();

        Assert.False(view.IsStale);
        Assert.Equal(TimeSpan.FromHours(2).TotalSeconds, view.StationarySeconds!.Value, precision: 0);
    }

    [Fact]
    public async Task Silence_alone_makes_a_card_stale()
    {
        await using PresenceHarness harness = await PresenceHarness.StartAsync();

        await harness.FixAsync(50, speedMetersPerSecond: 0);
        harness.Advance(harness.Options.StaleAfter + TimeSpan.FromMinutes(1));

        PresenceView view = await harness.ViewAsync();

        Assert.True(view.IsStale);
    }

    // ---- The stop clock ----

    [Fact]
    public async Task A_fix_too_vague_to_resolve_the_threshold_does_not_restart_the_stop_clock()
    {
        // A phone sitting indoors at work drifted across a 143 m band on fixes accurate to 100 m.
        // One of them read far enough from the anchor to restart the clock, throwing away two hours
        // and eleven minutes of genuine dwell and reporting the owner as newly arrived.
        await using PresenceHarness harness = await PresenceHarness.StartAsync();

        await harness.FixAsync(32_700, speedMetersPerSecond: 0, accuracyMeters: 12);
        harness.Advance(TimeSpan.FromHours(2));

        // 250 m from the anchor, which clears the 200 m threshold, on a fix that cannot resolve it.
        await harness.FixAsync(32_950, speedMetersPerSecond: 0.43, accuracyMeters: 100);

        PresenceView view = await harness.ViewAsync();

        Assert.Equal(TimeSpan.FromHours(2).TotalSeconds, view.StationarySeconds!.Value, precision: 0);
    }

    [Fact]
    public async Task Relocating_restarts_the_stop_clock_but_moving_on_the_spot_does_not()
    {
        // A phone lying still indoors reported 2 m/s and discarded a clock that had legitimately
        // been running for forty minutes.
        await using PresenceHarness harness = await PresenceHarness.StartAsync();

        await harness.FixAsync(50, speedMetersPerSecond: 0);
        harness.Advance(TimeSpan.FromMinutes(40));
        await harness.FixAsync(60, speedMetersPerSecond: 2, accuracyMeters: 5);

        PresenceView view = await harness.ViewAsync();

        Assert.Equal(TimeSpan.FromMinutes(40).TotalSeconds, view.StationarySeconds!.Value, precision: 0);

        harness.Advance(TimeSpan.FromMinutes(5));
        await harness.FixAsync(3_000, speedMetersPerSecond: 0, accuracyMeters: 5);
        harness.Advance(harness.Options.DwellDebounce + TimeSpan.FromMinutes(1));

        PresenceView relocated = await harness.ViewAsync();

        Assert.Equal(
            (harness.Options.DwellDebounce + TimeSpan.FromMinutes(1)).TotalSeconds,
            relocated.StationarySeconds!.Value,
            precision: 0);
    }
}
