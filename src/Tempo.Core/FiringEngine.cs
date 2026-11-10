namespace Tempo.Core;

public enum FireKind
{
    /// <summary>Nothing to deliver this evaluation.</summary>
    None = 0,
    /// <summary>A normally scheduled, on-time occurrence.</summary>
    Scheduled = 1,
    /// <summary>An occurrence whose time passed while Tempo was not running / asleep.</summary>
    Missed = 2,
    /// <summary>A snooze timer elapsed.</summary>
    Snooze = 3,
}

public readonly record struct FireDecision(FireKind Kind, DateTime SlotUtc, bool PlaySound)
{
    public static readonly FireDecision None = new(FireKind.None, default, false);
    public bool ShouldDeliver => Kind is FireKind.Scheduled or FireKind.Missed or FireKind.Snooze;
}

/// <summary>
/// Advances an alert's runtime state for the current instant and decides whether to deliver it.
/// Mutates the passed <see cref="Alert"/> (occurrence counters, last-fired markers, completion,
/// snooze). Deterministic and free of UI / IO so it can be unit-tested.
/// </summary>
public static class FiringEngine
{
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(60);

    public static FireDecision Evaluate(
        Alert a, DateTime nowUtc, TimeZoneInfo tz,
        MissedReminderPolicy policy, TimeSpan? grace = null)
    {
        var g = grace ?? DefaultGrace;

        // Paused alerts never fire and drop any pending snooze.
        if (!a.IsEnabled)
        {
            a.SnoozeUntilUtc = null;
            return FireDecision.None;
        }

        // A one-shot snooze takes priority over the regular schedule.
        if (a.SnoozeUntilUtc is DateTime snoozeUntil && nowUtc >= snoozeUntil)
        {
            a.SnoozeUntilUtc = null;
            a.LastFiredUtc = nowUtc;
            a.UpdatedUtc = nowUtc;
            return new FireDecision(FireKind.Snooze, snoozeUntil, a.PlaySound);
        }

        if (a.IsCompleted) return FireDecision.None;

        var slot = ScheduleCalculator.GetNextOccurrenceUtc(a, tz, nowUtc);
        if (slot is null)
        {
            a.IsCompleted = true;
            a.UpdatedUtc = nowUtc;
            return FireDecision.None;
        }

        if (slot > nowUtc)
            return FireDecision.None; // not due yet

        // Due now, or overdue because Tempo was not running.
        var mostRecent = ScheduleCalculator.MostRecentOnOrBefore(a, nowUtc, tz) ?? slot.Value;
        bool missed = (nowUtc - slot.Value) > g;
        bool deliver = !missed || policy == MissedReminderPolicy.ShowOnce;
        var kind = missed ? FireKind.Missed : FireKind.Scheduled;

        // Skip the whole backlog of missed slots: advance to the most recent one.
        a.LastFiredOccurrenceUtc = mostRecent;
        a.LastFiredUtc = nowUtc;
        a.UpdatedUtc = nowUtc;
        if (deliver) a.OccurrenceCount++;

        // Recompute completion.
        if (a.MaxOccurrences is int max && a.OccurrenceCount >= max)
            a.IsCompleted = true;
        else if (ScheduleCalculator.GetNextOccurrenceUtc(a, tz, nowUtc) is null)
            a.IsCompleted = true;

        return deliver
            ? new FireDecision(kind, mostRecent, a.PlaySound)
            : FireDecision.None;
    }

    public static void Snooze(Alert a, DateTime nowUtc, int minutes)
    {
        a.SnoozeUntilUtc = nowUtc.AddMinutes(Math.Max(1, minutes));
        a.UpdatedUtc = nowUtc;
    }

    public static void Dismiss(Alert a, DateTime nowUtc)
    {
        a.SnoozeUntilUtc = null;
        a.UpdatedUtc = nowUtc;
    }
}
