namespace Tempo.Core;

/// <summary>
/// Pure, side-effect-free recurrence math for <see cref="Alert"/>.
/// </summary>
/// <remarks>
/// Calendar schedules (<see cref="RepeatType.Daily"/>, <see cref="RepeatType.Weekly"/>) are evaluated
/// against a wall-clock time in a given <see cref="TimeZoneInfo"/>, so "daily at 09:00" stays at 09:00
/// across daylight-saving transitions. Elapsed schedules (<see cref="RepeatType.Custom"/>) are measured
/// as absolute durations from <see cref="Alert.AnchorUtc"/>, so "every 24 hours" keeps a constant spacing
/// regardless of DST (its wall-clock time may shift by an hour, which is the correct behaviour for an
/// elapsed interval).
/// </remarks>
public static class ScheduleCalculator
{
    private const int LookaheadDays = 366 * 2;

    /// <summary>
    /// The next scheduled slot (UTC) this alert should fire, taking into account the last delivered
    /// occurrence, the end date and the maximum-occurrence limit. Returns <c>null</c> when the alert
    /// has no further occurrences (and should be marked completed). Snooze is handled separately.
    /// </summary>
    public static DateTime? GetNextOccurrenceUtc(Alert a, TimeZoneInfo tz, DateTime? nowUtc = null)
    {
        if (a.MaxOccurrences is int max && a.OccurrenceCount >= max)
            return null;

        DateTime? slot = a.LastFiredOccurrenceUtc.HasValue
            ? NextAfter(a, a.LastFiredOccurrenceUtc.Value, tz)
            : NextOnOrAfter(a, EffectiveStartUtc(a, tz), tz);

        return slot;
    }

    /// <summary>
    /// The next moment the user will actually be alerted, including a pending snooze. Used for the
    /// "next occurrence" display and for sorting the Upcoming list. Returns <c>null</c> when nothing
    /// is scheduled.
    /// </summary>
    public static DateTime? GetNextFireUtc(Alert a, TimeZoneInfo tz, DateTime nowUtc)
    {
        if (!a.IsEnabled || a.IsCompleted)
        {
            // A paused alert with a pending snooze still has no scheduled fire while paused.
            return null;
        }

        DateTime? scheduled = GetNextOccurrenceUtc(a, tz, nowUtc);
        DateTime? snooze = (a.SnoozeUntilUtc is DateTime s && s > nowUtc) ? s : null;

        return Min(scheduled, snooze);
    }

    /// <summary>Smallest occurrence at or after <paramref name="instantUtc"/> (time + end-date only).</summary>
    public static DateTime? NextOnOrAfter(Alert a, DateTime instantUtc, TimeZoneInfo tz)
    {
        switch (a.Repeat)
        {
            case RepeatType.Once:
                if (a.OnceLocal is not DateTime once) return null;
                var onceUtc = SafeLocalToUtc(once, tz);
                return onceUtc >= instantUtc ? onceUtc : null;

            case RepeatType.Daily:
                return ScanCalendar(a, instantUtc, tz, forward: true, requireWeekday: false);

            case RepeatType.Weekly:
                if (a.Days == WeekDays.None) return null;
                return ScanCalendar(a, instantUtc, tz, forward: true, requireWeekday: true);

            case RepeatType.Custom:
                return CustomOnOrAfter(a, instantUtc, tz);

            default:
                return null;
        }
    }

    /// <summary>Smallest occurrence strictly after <paramref name="instantUtc"/>.</summary>
    public static DateTime? NextAfter(Alert a, DateTime instantUtc, TimeZoneInfo tz)
        => NextOnOrAfter(a, instantUtc.AddTicks(1), tz);

    /// <summary>
    /// Largest occurrence at or before <paramref name="whenUtc"/>, not earlier than the schedule's
    /// effective start. Used to skip a backlog of missed occurrences.
    /// </summary>
    public static DateTime? MostRecentOnOrBefore(Alert a, DateTime whenUtc, TimeZoneInfo tz)
    {
        var start = EffectiveStartUtc(a, tz);
        switch (a.Repeat)
        {
            case RepeatType.Once:
                if (a.OnceLocal is not DateTime once) return null;
                var onceUtc = SafeLocalToUtc(once, tz);
                return onceUtc <= whenUtc ? onceUtc : null;

            case RepeatType.Daily:
                return ScanCalendar(a, whenUtc, tz, forward: false, requireWeekday: false, lowerBoundUtc: start);

            case RepeatType.Weekly:
                if (a.Days == WeekDays.None) return null;
                return ScanCalendar(a, whenUtc, tz, forward: false, requireWeekday: true, lowerBoundUtc: start);

            case RepeatType.Custom:
                var interval = IntervalToTimeSpan(a);
                if (interval <= TimeSpan.Zero || whenUtc < a.AnchorUtc) return null;
                long k = (whenUtc - a.AnchorUtc).Ticks / interval.Ticks;
                var occ = a.AnchorUtc + TimeSpan.FromTicks(k * interval.Ticks);
                return occ <= whenUtc ? occ : null;

            default:
                return null;
        }
    }

    /// <summary>How many scheduled occurrences fall in the half-open window (afterUtc, throughUtc].</summary>
    public static int CountOccurrences(Alert a, DateTime afterUtc, DateTime throughUtc, TimeZoneInfo tz, int cap = 10000)
    {
        int count = 0;
        var cursor = afterUtc;
        while (count < cap)
        {
            var next = NextAfter(a, cursor, tz);
            if (next is not DateTime n || n > throughUtc) break;
            count++;
            cursor = n;
        }
        return count;
    }

    public static TimeSpan IntervalToTimeSpan(Alert a) => a.IntervalUnit switch
    {
        IntervalUnit.Minutes => TimeSpan.FromMinutes(a.IntervalCount),
        IntervalUnit.Hours => TimeSpan.FromHours(a.IntervalCount),
        IntervalUnit.Days => TimeSpan.FromDays(a.IntervalCount),
        _ => TimeSpan.Zero,
    };

    // --- internals ---

    private static DateTime EffectiveStartUtc(Alert a, TimeZoneInfo tz) => a.Repeat switch
    {
        RepeatType.Once => a.OnceLocal is DateTime o ? SafeLocalToUtc(o, tz) : DateTime.MaxValue,
        RepeatType.Custom => a.AnchorUtc,
        _ => a.CreatedUtc,
    };

    private static DateTime? CustomOnOrAfter(Alert a, DateTime instantUtc, TimeZoneInfo tz)
    {
        var interval = IntervalToTimeSpan(a);
        if (interval <= TimeSpan.Zero) return null;

        DateTime occ;
        if (instantUtc <= a.AnchorUtc)
        {
            occ = a.AnchorUtc;
        }
        else
        {
            var elapsed = instantUtc - a.AnchorUtc;
            long k = (long)Math.Ceiling(elapsed.Ticks / (double)interval.Ticks);
            occ = a.AnchorUtc + TimeSpan.FromTicks(k * interval.Ticks);
            while (occ < instantUtc) occ += interval;
        }

        if (a.EndDateLocal is DateTime end)
        {
            var localOcc = TimeZoneInfo.ConvertTimeFromUtc(occ, tz);
            if (localOcc.Date > end.Date) return null;
        }
        return occ;
    }

    private static DateTime? ScanCalendar(
        Alert a, DateTime pivotUtc, TimeZoneInfo tz, bool forward, bool requireWeekday,
        DateTime? lowerBoundUtc = null)
    {
        var localPivot = TimeZoneInfo.ConvertTimeFromUtc(pivotUtc, tz);
        var startDate = localPivot.Date;
        var timeOfDay = TimeSpan.FromMinutes(a.TimeOfDayMinutes);

        for (int i = 0; i <= LookaheadDays; i++)
        {
            var date = forward ? startDate.AddDays(i) : startDate.AddDays(-i);

            if (requireWeekday && !a.Days.HasFlag(ToWeekDays(date.DayOfWeek)))
                continue;

            if (forward && a.EndDateLocal is DateTime end && date > end.Date)
                return null;

            var localSlot = DateTime.SpecifyKind(date + timeOfDay, DateTimeKind.Unspecified);
            var occ = SafeLocalToUtc(localSlot, tz);

            if (forward)
            {
                if (occ >= pivotUtc) return occ;
            }
            else
            {
                if (occ <= pivotUtc)
                {
                    if (lowerBoundUtc is DateTime lb && occ < lb) return null;
                    return occ;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Converts a wall-clock local time to UTC, nudging forward out of a spring-forward gap
    /// (an invalid local time) so the alert still fires rather than throwing.
    /// </summary>
    public static DateTime SafeLocalToUtc(DateTime local, TimeZoneInfo tz)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        int guard = 0;
        while (tz.IsInvalidTime(local) && guard++ < 16)
            local = local.AddMinutes(15);
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    private static WeekDays ToWeekDays(DayOfWeek d) => d switch
    {
        DayOfWeek.Sunday => WeekDays.Sunday,
        DayOfWeek.Monday => WeekDays.Monday,
        DayOfWeek.Tuesday => WeekDays.Tuesday,
        DayOfWeek.Wednesday => WeekDays.Wednesday,
        DayOfWeek.Thursday => WeekDays.Thursday,
        DayOfWeek.Friday => WeekDays.Friday,
        DayOfWeek.Saturday => WeekDays.Saturday,
        _ => WeekDays.None,
    };

    private static DateTime? Min(DateTime? x, DateTime? y)
    {
        if (x is null) return y;
        if (y is null) return x;
        return x < y ? x : y;
    }
}
