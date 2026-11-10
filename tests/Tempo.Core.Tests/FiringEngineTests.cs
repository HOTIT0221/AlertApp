using Tempo.Core;
using Xunit;

namespace Tempo.Core.Tests;

public class FiringEngineTests
{
    private static TimeZoneInfo Tz => TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    private static DateTime Utc(int y, int m, int d, int h, int min) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    [Fact]
    public void OnTimeDaily_Delivers_AndAdvances()
    {
        var a = new Alert { Repeat = RepeatType.Daily, TimeOfDayMinutes = 9 * 60, CreatedUtc = Utc(2026, 6, 1, 0, 0) };
        var now = Utc(2026, 6, 1, 13, 0); // 09:00 EDT exactly

        var d = FiringEngine.Evaluate(a, now, Tz, MissedReminderPolicy.ShowOnce);
        Assert.Equal(FireKind.Scheduled, d.Kind);
        Assert.Equal(1, a.OccurrenceCount);
        Assert.Equal(Utc(2026, 6, 1, 13, 0), a.LastFiredOccurrenceUtc);
        Assert.False(a.IsCompleted);

        // Immediately re-evaluating does not double-fire.
        var d2 = FiringEngine.Evaluate(a, now, Tz, MissedReminderPolicy.ShowOnce);
        Assert.Equal(FireKind.None, d2.Kind);
        Assert.Equal(1, a.OccurrenceCount);
    }

    [Fact]
    public void Once_CompletesAfterDelivery()
    {
        var a = new Alert { Repeat = RepeatType.Once, OnceLocal = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Unspecified) };
        var now = Utc(2026, 6, 1, 13, 0);

        var d = FiringEngine.Evaluate(a, now, Tz, MissedReminderPolicy.ShowOnce);
        Assert.Equal(FireKind.Scheduled, d.Kind);
        Assert.True(a.IsCompleted);
    }

    [Fact]
    public void Missed_ShowOnce_DeliversOnceAndSkipsBacklog()
    {
        // Every 10 minutes; app was off for ~2 hours.
        var anchor = Utc(2026, 6, 1, 10, 0);
        var a = new Alert
        {
            Repeat = RepeatType.Custom, IntervalUnit = IntervalUnit.Minutes, IntervalCount = 10,
            AnchorUtc = anchor, LastFiredOccurrenceUtc = anchor,
        };
        var now = Utc(2026, 6, 1, 12, 3); // many slots passed since 10:00

        var d = FiringEngine.Evaluate(a, now, Tz, MissedReminderPolicy.ShowOnce);
        Assert.Equal(FireKind.Missed, d.Kind);
        // Advanced to the most recent slot <= now (12:00), not firing each of the ~12 missed slots.
        Assert.Equal(Utc(2026, 6, 1, 12, 0), a.LastFiredOccurrenceUtc);
        Assert.Equal(1, a.OccurrenceCount);
    }

    [Fact]
    public void Missed_Skip_DoesNotDeliver_ButAdvances()
    {
        var anchor = Utc(2026, 6, 1, 10, 0);
        var a = new Alert
        {
            Repeat = RepeatType.Custom, IntervalUnit = IntervalUnit.Minutes, IntervalCount = 10,
            AnchorUtc = anchor, LastFiredOccurrenceUtc = anchor,
        };
        var now = Utc(2026, 6, 1, 12, 3);

        var d = FiringEngine.Evaluate(a, now, Tz, MissedReminderPolicy.Skip);
        Assert.Equal(FireKind.None, d.Kind);
        Assert.Equal(Utc(2026, 6, 1, 12, 0), a.LastFiredOccurrenceUtc);
        Assert.Equal(0, a.OccurrenceCount);
    }

    [Fact]
    public void Paused_DoesNotFire_AndClearsSnooze()
    {
        var a = new Alert
        {
            Repeat = RepeatType.Daily, TimeOfDayMinutes = 9 * 60, IsEnabled = false,
            SnoozeUntilUtc = Utc(2026, 6, 1, 12, 0),
        };
        var d = FiringEngine.Evaluate(a, Utc(2026, 6, 1, 13, 0), Tz, MissedReminderPolicy.ShowOnce);
        Assert.Equal(FireKind.None, d.Kind);
        Assert.Null(a.SnoozeUntilUtc);
    }

    [Fact]
    public void Snooze_DeliversAtSnoozeTime_WithoutCountingOccurrence()
    {
        var a = new Alert { Repeat = RepeatType.Daily, TimeOfDayMinutes = 9 * 60, CreatedUtc = Utc(2026, 6, 1, 0, 0) };
        FiringEngine.Snooze(a, Utc(2026, 6, 1, 13, 0), 10);
        Assert.Equal(Utc(2026, 6, 1, 13, 10), a.SnoozeUntilUtc);

        var d = FiringEngine.Evaluate(a, Utc(2026, 6, 1, 13, 10), Tz, MissedReminderPolicy.ShowOnce);
        Assert.Equal(FireKind.Snooze, d.Kind);
        Assert.Null(a.SnoozeUntilUtc);
        Assert.Equal(0, a.OccurrenceCount); // snooze is not a scheduled occurrence
    }

    [Fact]
    public void MaxOccurrences_CompletesAfterLastDelivery()
    {
        var a = new Alert
        {
            Repeat = RepeatType.Daily, TimeOfDayMinutes = 9 * 60, CreatedUtc = Utc(2026, 6, 1, 0, 0),
            MaxOccurrences = 1,
        };
        var d = FiringEngine.Evaluate(a, Utc(2026, 6, 1, 13, 0), Tz, MissedReminderPolicy.ShowOnce);
        Assert.Equal(FireKind.Scheduled, d.Kind);
        Assert.True(a.IsCompleted);
        Assert.Equal(1, a.OccurrenceCount);
    }
}
