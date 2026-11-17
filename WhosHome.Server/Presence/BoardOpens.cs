using WhosHome.Server.Data;

namespace WhosHome.Server.Presence;

/// <summary>
/// Whether household members actually look at the board, which nothing else can answer: there is no
/// request log, and sessions live in a cookie the server never records. Kept to a last-opened time
/// and a count of days, so it says how much the board is used without becoming a log of when.
/// </summary>
public static class BoardOpens
{
    /// <summary>The board polls every few seconds while open. Writing on each poll would cost a
    /// database write per poll per person for a timestamp nobody needs to the second.</summary>
    public static readonly TimeSpan WriteInterval = TimeSpan.FromMinutes(5);

    /// <summary>Records that <paramref name="person"/> has the board open at <paramref name="now"/>.
    /// Returns whether anything changed and needs saving.</summary>
    public static bool Record(Person person, DateTimeOffset now, TimeZoneInfo zone)
    {
        DateOnly today = LocalDay(now, zone);

        if (person.LastOpenedUtc is { } last)
        {
            bool sameDay = LocalDay(last, zone) == today;
            if (sameDay && now - last < WriteInterval)
            {
                return false;
            }

            if (!sameDay)
            {
                person.DaysOpened++;
            }
        }
        else
        {
            person.DaysOpened++;
        }

        person.LastOpenedUtc = now;
        return true;
    }

    private static DateOnly LocalDay(DateTimeOffset instant, TimeZoneInfo zone)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
    }
}
