namespace Tempo.Core;

/// <summary>How an alert repeats.</summary>
public enum RepeatType
{
    /// <summary>Fires one time at a specific local date and time.</summary>
    Once = 0,

    /// <summary>Fires every day at a fixed wall-clock time (calendar based, DST aware).</summary>
    Daily = 1,

    /// <summary>Fires on selected weekdays at a fixed wall-clock time (calendar based).</summary>
    Weekly = 2,

    /// <summary>Fires on an elapsed interval (every N minutes / hours / days) from an anchor instant.</summary>
    Custom = 3,
}

/// <summary>Unit for a <see cref="RepeatType.Custom"/> elapsed interval.</summary>
public enum IntervalUnit
{
    Minutes = 0,
    Hours = 1,
    Days = 2,
}

/// <summary>Selectable weekdays for <see cref="RepeatType.Weekly"/> alerts.</summary>
[Flags]
public enum WeekDays
{
    None = 0,
    Sunday = 1 << 0,
    Monday = 1 << 1,
    Tuesday = 1 << 2,
    Wednesday = 1 << 3,
    Thursday = 1 << 4,
    Friday = 1 << 5,
    Saturday = 1 << 6,

    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekend = Saturday | Sunday,
    All = Weekdays | Weekend,
}

/// <summary>
/// Policy for reminders whose scheduled time passed while Tempo was not running
/// (fully exited, asleep, or shut down).
/// </summary>
public enum MissedReminderPolicy
{
    /// <summary>Show a single "missed" reminder for the most recent missed occurrence, then resume.</summary>
    ShowOnce = 0,

    /// <summary>Silently skip anything that was missed and resume at the next future occurrence.</summary>
    Skip = 1,
}

/// <summary>What happens to the main window when the user clicks the window close button.</summary>
public enum CloseBehavior
{
    /// <summary>Hide to the system tray and keep reminders running (default).</summary>
    MinimizeToTray = 0,

    /// <summary>Fully exit Tempo (reminders stop until next launch).</summary>
    Exit = 1,
}

/// <summary>The clock format used when presenting times.</summary>
public enum ClockFormat
{
    /// <summary>Follow the Windows regional setting.</summary>
    System = 0,
    Hours12 = 1,
    Hours24 = 2,
}
