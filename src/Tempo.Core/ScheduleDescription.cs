using System.Globalization;

namespace Tempo.Core;

/// <summary>Builds short human-readable descriptions of an alert's schedule (UI-agnostic).</summary>
public static class ScheduleDescription
{
    public static string Describe(Alert a, bool use24Hour)
    {
        switch (a.Repeat)
        {
            case RepeatType.Once:
                return a.OnceLocal is DateTime o
                    ? "Once · " + o.ToString(use24Hour ? "ddd d MMM, HH:mm" : "ddd d MMM, h:mm tt", CultureInfo.CurrentCulture)
                    : "Once";

            case RepeatType.Daily:
                return "Daily";

            case RepeatType.Weekly:
                return "Weekly · " + DescribeDays(a.Days);

            case RepeatType.Custom:
                return "Every " + DescribeInterval(a.IntervalCount, a.IntervalUnit);

            default:
                return string.Empty;
        }
    }

    public static string TimeOfDayText(int minutes, bool use24Hour)
    {
        var t = TimeSpan.FromMinutes(minutes);
        var dt = DateTime.Today.Add(t);
        return dt.ToString(use24Hour ? "HH:mm" : "h:mm tt", CultureInfo.CurrentCulture);
    }

    public static string DescribeInterval(int count, IntervalUnit unit)
    {
        string unitName = unit switch
        {
            IntervalUnit.Minutes => count == 1 ? "minute" : "minutes",
            IntervalUnit.Hours => count == 1 ? "hour" : "hours",
            IntervalUnit.Days => count == 1 ? "day" : "days",
            _ => string.Empty,
        };
        return count == 1 ? unitName : $"{count} {unitName}";
    }

    public static string DescribeDays(WeekDays days)
    {
        if (days == WeekDays.None) return "—";
        if (days == WeekDays.All) return "Every day";
        if (days == WeekDays.Weekdays) return "Mon – Fri";
        if (days == WeekDays.Weekend) return "Sat – Sun";

        var order = new[]
        {
            (WeekDays.Monday, "Mon"), (WeekDays.Tuesday, "Tue"), (WeekDays.Wednesday, "Wed"),
            (WeekDays.Thursday, "Thu"), (WeekDays.Friday, "Fri"), (WeekDays.Saturday, "Sat"),
            (WeekDays.Sunday, "Sun"),
        };
        var parts = order.Where(p => days.HasFlag(p.Item1)).Select(p => p.Item2);
        return string.Join(", ", parts);
    }

    /// <summary>Friendly relative description of a future/past instant, e.g. "in 2h 5m" or "Today 09:00".</summary>
    public static string DescribeNext(DateTime? nextUtc, TimeZoneInfo tz, DateTime nowUtc, bool use24Hour)
    {
        if (nextUtc is not DateTime n) return "No upcoming time";

        var local = TimeZoneInfo.ConvertTimeFromUtc(n, tz);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
        var timeStr = local.ToString(use24Hour ? "HH:mm" : "h:mm tt", CultureInfo.CurrentCulture);

        var dayDiff = (local.Date - localNow.Date).Days;
        string dayStr = dayDiff switch
        {
            < 0 => "Overdue",
            0 => "Today",
            1 => "Tomorrow",
            < 7 => local.ToString("dddd", CultureInfo.CurrentCulture),
            _ => local.ToString("ddd d MMM", CultureInfo.CurrentCulture),
        };

        if (dayDiff < 0) return $"Overdue · was {timeStr}";
        return $"{dayStr} · {timeStr}";
    }
}
