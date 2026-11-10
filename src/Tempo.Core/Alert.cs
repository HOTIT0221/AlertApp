namespace Tempo.Core;

/// <summary>
/// A reminder definition plus its runtime scheduling state.
/// </summary>
/// <remarks>
/// Times are stored as UTC except where a value represents a wall-clock local time:
/// <list type="bullet">
/// <item><see cref="OnceLocal"/> and <see cref="EndDateLocal"/> are local wall-clock values
/// (Kind = Unspecified) so that calendar schedules survive DST transitions.</item>
/// <item><see cref="TimeOfDayMinutes"/> is minutes from local midnight.</item>
/// <item><see cref="AnchorUtc"/>, <see cref="LastFiredUtc"/>, <see cref="LastFiredOccurrenceUtc"/>
/// and <see cref="SnoozeUntilUtc"/> are absolute UTC instants.</item>
/// </list>
/// </remarks>
public sealed class Alert
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string? Message { get; set; }

    /// <summary>Active (true) vs. paused (false). Paused alerts never fire.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>A one-time alert that has already fired, or a recurring alert that has reached its end/limit.</summary>
    public bool IsCompleted { get; set; }

    public bool PlaySound { get; set; } = true;

    public RepeatType Repeat { get; set; } = RepeatType.Once;

    // --- Calendar schedules (Daily / Weekly) ---

    /// <summary>Minutes from local midnight for Daily/Weekly (and the time component of Once editing UI).</summary>
    public int TimeOfDayMinutes { get; set; }

    /// <summary>Selected weekdays for <see cref="RepeatType.Weekly"/>.</summary>
    public WeekDays Days { get; set; } = WeekDays.None;

    // --- One-time ---

    /// <summary>Local wall-clock date+time for a one-time alert (Kind = Unspecified).</summary>
    public DateTime? OnceLocal { get; set; }

    // --- Custom elapsed interval ---

    public int IntervalCount { get; set; } = 1;

    public IntervalUnit IntervalUnit { get; set; } = IntervalUnit.Hours;

    /// <summary>The instant the elapsed-interval schedule is measured from.</summary>
    public DateTime AnchorUtc { get; set; } = DateTime.UtcNow;

    // --- Limits ---

    /// <summary>Optional inclusive last local date on which the alert may fire.</summary>
    public DateTime? EndDateLocal { get; set; }

    /// <summary>Optional maximum number of (non-snooze) deliveries.</summary>
    public int? MaxOccurrences { get; set; }

    /// <summary>How many real occurrences have been delivered.</summary>
    public int OccurrenceCount { get; set; }

    // --- Runtime state ---

    public DateTime? LastFiredUtc { get; set; }

    /// <summary>The scheduled slot (UTC) most recently delivered. Used to prevent duplicate delivery.</summary>
    public DateTime? LastFiredOccurrenceUtc { get; set; }

    /// <summary>When set in the future, a one-shot snooze delivery is pending at this instant.</summary>
    public DateTime? SnoozeUntilUtc { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public Alert Clone() => (Alert)MemberwiseClone();
}
