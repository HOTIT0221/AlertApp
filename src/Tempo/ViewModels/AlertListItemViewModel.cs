using System.Globalization;
using Microsoft.UI.Xaml;
using Tempo.Core;
using Tempo.Services;

namespace Tempo.ViewModels;

/// <summary>Read-only presentation wrapper around an <see cref="Alert"/> for list cards.</summary>
public sealed class AlertListItemViewModel
{
    public Alert Model { get; }

    public Guid Id => Model.Id;
    public string Title => string.IsNullOrWhiteSpace(Model.Title) ? "(Untitled reminder)" : Model.Title;
    public string Message => Model.Message ?? string.Empty;
    public bool HasMessage => !string.IsNullOrWhiteSpace(Model.Message);

    public string TimeText { get; }
    public string ScheduleText { get; }
    public string NextText { get; }
    public bool IsEnabled => Model.IsEnabled;
    public bool IsCompleted => Model.IsCompleted;
    public bool PlaySound => Model.PlaySound;
    public bool ShowSchedule => Model.Repeat != RepeatType.Once || !Model.IsCompleted;

    public Visibility MessageVisibility => HasMessage ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ScheduleVisibility => ShowSchedule ? Visibility.Visible : Visibility.Collapsed;

    public string StatusText => Model.IsCompleted ? "Completed" : Model.IsEnabled ? "Active" : "Paused";

    private AlertListItemViewModel(Alert a, string timeText, string scheduleText, string nextText)
    {
        Model = a;
        TimeText = timeText;
        ScheduleText = scheduleText;
        NextText = nextText;
    }

    public static AlertListItemViewModel Create(Alert a, AlertService service, bool use24Hour)
    {
        string time = a.Repeat switch
        {
            RepeatType.Custom => "Every " + ShortInterval(a),
            RepeatType.Once when a.OnceLocal is DateTime o =>
                o.ToString(use24Hour ? "HH:mm" : "h:mm tt", CultureInfo.CurrentCulture),
            _ => ScheduleDescription.TimeOfDayText(a.TimeOfDayMinutes, use24Hour),
        };

        string schedule = ScheduleDescription.Describe(a, use24Hour);

        string next;
        if (a.IsCompleted)
            next = a.Repeat == RepeatType.Once ? "Delivered" : "Finished";
        else if (!a.IsEnabled)
            next = "Paused";
        else
        {
            var nextUtc = service.NextFireUtc(a);
            next = ScheduleDescription.DescribeNext(nextUtc, service.TimeZone, DateTime.UtcNow, use24Hour);
        }

        return new AlertListItemViewModel(a, time, schedule, next);
    }

    private static string ShortInterval(Alert a)
    {
        string unit = a.IntervalUnit switch
        {
            IntervalUnit.Minutes => "min",
            IntervalUnit.Hours => "h",
            IntervalUnit.Days => "d",
            _ => "",
        };
        return a.IntervalCount == 1 && a.IntervalUnit != IntervalUnit.Minutes
            ? "1" + unit
            : $"{a.IntervalCount}{unit}";
    }
}
