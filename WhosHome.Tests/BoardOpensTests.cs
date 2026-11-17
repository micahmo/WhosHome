using WhosHome.Server.Data;
using WhosHome.Server.Presence;

namespace WhosHome.Tests;

/// <summary>
/// The admin page's "opened the board on N days". The board polls every few seconds while open, so
/// the point of these is that polling cannot inflate either number.
/// </summary>
public class BoardOpensTests
{
    private static readonly TimeZoneInfo Eastern =
        TimeZoneInfo.CreateCustomTimeZone("Test Eastern", TimeSpan.FromHours(-5), "Test Eastern", "Test Eastern");

    // 9:00 AM in the test zone.
    private static readonly DateTimeOffset Morning = new(2026, 11, 17, 14, 0, 0, TimeSpan.Zero);

    private static Person NewPerson() => new() { Name = "Alaina", DeviceId = "device" };

    [Fact]
    public void The_first_open_counts_a_day()
    {
        Person person = NewPerson();

        Assert.True(BoardOpens.Record(person, Morning, Eastern));

        Assert.Equal(Morning, person.LastOpenedUtc);
        Assert.Equal(1, person.DaysOpened);
    }

    [Fact]
    public void Polls_while_the_board_stays_open_write_nothing()
    {
        Person person = NewPerson();
        BoardOpens.Record(person, Morning, Eastern);

        Assert.False(BoardOpens.Record(person, Morning + TimeSpan.FromSeconds(30), Eastern));
        Assert.False(BoardOpens.Record(person, Morning + BoardOpens.WriteInterval - TimeSpan.FromSeconds(1), Eastern));

        Assert.Equal(Morning, person.LastOpenedUtc);
        Assert.Equal(1, person.DaysOpened);
    }

    [Fact]
    public void A_later_open_the_same_day_moves_the_time_but_not_the_count()
    {
        Person person = NewPerson();
        BoardOpens.Record(person, Morning, Eastern);
        DateTimeOffset evening = Morning + TimeSpan.FromHours(10);

        Assert.True(BoardOpens.Record(person, evening, Eastern));

        Assert.Equal(evening, person.LastOpenedUtc);
        Assert.Equal(1, person.DaysOpened);
    }

    [Fact]
    public void Days_follow_the_household_clock_not_UTC()
    {
        // 9 AM and 11 PM local straddle midnight UTC but are one day at home.
        Person person = NewPerson();
        BoardOpens.Record(person, Morning, Eastern);
        BoardOpens.Record(person, Morning + TimeSpan.FromHours(14), Eastern);

        Assert.Equal(1, person.DaysOpened);
    }

    [Fact]
    public void Midnight_starts_a_new_day_even_inside_the_write_interval()
    {
        Person person = NewPerson();
        DateTimeOffset beforeMidnight = Morning + TimeSpan.FromHours(15) - TimeSpan.FromMinutes(1);
        BoardOpens.Record(person, beforeMidnight, Eastern);

        Assert.True(BoardOpens.Record(person, beforeMidnight + TimeSpan.FromMinutes(2), Eastern));

        Assert.Equal(2, person.DaysOpened);
    }
}
