using Tempo.Core;
using Xunit;

namespace Tempo.Core.Tests;

public class ScheduleCalculatorTests
{
    // A fixed IANA/Windows zone that observes DST. Tests run on Windows.
    private static TimeZoneInfo Tz => TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    private static DateTime Utc(int y, int m, int d, int h, int min) =>
        new(y, m, d, h, min, 0, DateTimeKind.Utc);

    private static DateTime LocalWall(int y, int m, int d, int h, int min) =>
        new(y, m, d, h, min, 0, DateTimeKind.Unspecified);

    [Fact]
    public void Once_ReturnsSingleOccurrence_ThenNone()
    {
        var a = new Alert
        {
            Repeat = RepeatType.Once,
            OnceLocal = LocalWall(2026, 6, 1, 9, 0),
        };

        var next = ScheduleCalculator.GetNextOccurrenceUtc(a, Tz);
        Assert.NotNull(next);
        // 09:00 EDT (UTC-4 in June) == 13:00 UTC
        Assert.Equal(Utc(2026, 6, 1, 13, 0), next);

        a.LastFiredOccurrenceUtc = next;
        Assert.Null(ScheduleCalculator.GetNextOccurrenceUtc(a, Tz));
    }

    [Fact]
    public void Daily_FiresSameDayWhenCreatedBeforeTime()
    {
        var a = new Alert
        {
            Repeat = RepeatType.Daily,
            TimeOfDayMinutes = 9 * 60,
            CreatedUtc = Utc(2026, 6, 1, 8, 0), // 04:00 EDT local, before 09:00
        };
        var next = ScheduleCalculator.GetNextOccurrenceUtc(a, Tz);
        Assert.Equal(Utc(2026, 6, 1, 13, 0), next); // today 09:00 EDT
    }

    [Fact]
    public void Daily_RollsToNextDay_AfterFiring()
    {
        var a = new Alert
        {
            Repeat = RepeatType.Daily,
            TimeOfDayMinutes = 9 * 60,
            CreatedUtc = Utc(2026, 6, 1, 8, 0),
            LastFiredOccurrenceUtc = Utc(2026, 6, 1, 13, 0),
        };
        var next = ScheduleCalculator.GetNextOccurrenceUtc(a, Tz);
        Assert.Equal(Utc(2026, 6, 2, 13, 0), next);
    }

    [Fact]
    public void Daily_KeepsWallClockAcrossDstSpringForward()
    {
        // US DST begins 2026-03-08. Before: EST (UTC-5). After: EDT (UTC-4).
        var a = new Alert
        {
            Repeat = RepeatType.Daily,
            TimeOfDayMinutes = 9 * 60,
            CreatedUtc = Utc(2026, 3, 6, 0, 0),
            LastFiredOccurrenceUtc = Utc(2026, 3, 7, 14, 0), // Sat 09:00 EST = 14:00 UTC
        };
        var next = ScheduleCalculator.GetNextOccurrenceUtc(a, Tz);
        // Sunday 2026-03-08 09:00 local is EDT => 13:00 UTC (wall clock preserved at 09:00)
        Assert.Equal(Utc(2026, 3, 8, 13, 0), next);
    }

    [Fact]
    public void Weekly_PicksNextSelectedWeekday()
    {
        // 2026-06-01 is a Monday.
        var a = new Alert
        {
            Repeat = RepeatType.Weekly,
            TimeOfDayMinutes = 9 * 60,
            Days = WeekDays.Tuesday | WeekDays.Thursday,
            CreatedUtc = Utc(2026, 6, 1, 8, 0),
        };
        var next = ScheduleCalculator.GetNextOccurrenceUtc(a, Tz);
        Assert.Equal(Utc(2026, 6, 2, 13, 0), next); // Tuesday 09:00 EDT

        a.LastFiredOccurrenceUtc = next;
        var after = ScheduleCalculator.GetNextOccurrenceUtc(a, Tz);
        Assert.Equal(Utc(2026, 6, 4, 13, 0), after); // Thursday
    }

    [Fact]
    public void Custom_Hours_IsElapsedInterval()
    {
        var anchor = Utc(2026, 6, 1, 12, 0);
        var a = new Alert
        {
            Repeat = RepeatType.Custom,
            IntervalUnit = IntervalUnit.Hours,
            IntervalCount = 2,
            AnchorUtc = anchor,
        };
        // First occurrence is the anchor itself.
        Assert.Equal(anchor, ScheduleCalculator.GetNextOccurrenceUtc(a, Tz));

        a.LastFiredOccurrenceUtc = anchor;
        Assert.Equal(anchor.AddHours(2), ScheduleCalculator.GetNextOccurrenceUtc(a, Tz));
    }

    [Fact]
    public void Custom_Minutes_ComputesForwardFromAnchor()
    {
        var anchor = Utc(2026, 6, 1, 12, 0);
        var a = new Alert
        {
            Repeat = RepeatType.Custom,
            IntervalUnit = IntervalUnit.Minutes,
            IntervalCount = 15,
            AnchorUtc = anchor,
            LastFiredOccurrenceUtc = anchor,
        };
        var next = ScheduleCalculator.NextOnOrAfter(a, Utc(2026, 6, 1, 12, 40), Tz);
        Assert.Equal(Utc(2026, 6, 1, 12, 45), next);
    }

    [Fact]
    public void EndDate_StopsSchedule()
    {
        var a = new Alert
        {
            Repeat = RepeatType.Daily,
            TimeOfDayMinutes = 9 * 60,
            CreatedUtc = Utc(2026, 6, 1, 8, 0),
            EndDateLocal = LocalWall(2026, 6, 2, 0, 0),
            LastFiredOccurrenceUtc = Utc(2026, 6, 2, 13, 0), // fired the last allowed day
        };
        Assert.Null(ScheduleCalculator.GetNextOccurrenceUtc(a, Tz));
    }

    [Fact]
    public void MaxOccurrences_StopsSchedule()
    {
        var a = new Alert
        {
            Repeat = RepeatType.Daily,
            TimeOfDayMinutes = 9 * 60,
            MaxOccurrences = 3,
            OccurrenceCount = 3,
        };
        Assert.Null(ScheduleCalculator.GetNextOccurrenceUtc(a, Tz));
    }

    [Fact]
    public void MostRecentOnOrBefore_FindsBacklogAnchor()
    {
        var anchor = Utc(2026, 6, 1, 12, 0);
        var a = new Alert
        {
            Repeat = RepeatType.Custom,
            IntervalUnit = IntervalUnit.Minutes,
            IntervalCount = 30,
            AnchorUtc = anchor,
        };
        var mr = ScheduleCalculator.MostRecentOnOrBefore(a, Utc(2026, 6, 1, 14, 20), Tz);
        Assert.Equal(Utc(2026, 6, 1, 14, 0), mr);
    }
}
