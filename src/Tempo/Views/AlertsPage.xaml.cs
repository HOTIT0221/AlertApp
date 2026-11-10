using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Tempo.Core;
using Tempo.Services;
using Tempo.ViewModels;

namespace Tempo.Views;

public sealed partial class AlertsPage : Page
{
    private AlertService Service => App.Instance!.Alerts;
    private bool _use24;
    private string _routine = "Daily";
    private string _filter = "All";
    private Guid? _editingId;
    private Alert? _lastDeleted;
    private DispatcherTimer? _undoTimer;

    public AlertsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _use24 = ClockFormatHelper.Use24Hour(Service.Settings.ClockFormat);
        TimeInput.ClockIdentifier = _use24 ? "24HourClock" : "12HourClock";
        if (TimeInput.SelectedTime is null)
            TimeInput.SelectedTime = new TimeSpan(9, 0, 0);

        Service.AlertsChanged += Refresh;

        SelectSegment("Daily");
        SelectFilter("All");
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Service.AlertsChanged -= Refresh;
        _undoTimer?.Stop();
    }

    // --- Routine segment ---

    private void OnSegmentClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag) SelectSegment(tag);
    }

    private void SelectSegment(string routine)
    {
        _routine = routine;
        foreach (var (btn, tag) in new[] { (SegOnce, "Once"), (SegDaily, "Daily"), (SegWeekly, "Weekly"), (SegCustom, "Custom") })
        {
            bool sel = tag == routine;
            btn.Background = sel ? new SolidColorBrush(Colors.White) : Transparent();
            btn.Foreground = sel ? Res("AccentBrush") : Res("TextSecondaryBrush");
        }

        OncePanel.Visibility = routine == "Once" ? Visibility.Visible : Visibility.Collapsed;
        WeeklyPanel.Visibility = routine == "Weekly" ? Visibility.Visible : Visibility.Collapsed;
        CustomPanel.Visibility = routine == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        TimeField.Visibility = routine == "Custom" ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnDayToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton t)
        {
            bool on = t.IsChecked == true;
            t.Background = on ? Res("AccentBrush") : new SolidColorBrush(Colors.White);
            t.Foreground = on ? new SolidColorBrush(Colors.White) : Res("TextSecondaryBrush");
            t.BorderBrush = on ? Res("AccentBrush") : Res("BorderBrush");
        }
    }

    private void OnFormChanged(object sender, TextChangedEventArgs e)
    {
        if (FormError.IsOpen) FormError.IsOpen = false;
    }

    private void OnEndsChanged(object sender, RoutedEventArgs e)
    {
        // Can fire during InitializeComponent before later-declared fields exist.
        if (EndDateInput is null || EndCountInput is null) return;
        int i = EndsInput.SelectedIndex;
        EndDateInput.Visibility = i == 1 ? Visibility.Visible : Visibility.Collapsed;
        EndCountInput.Visibility = i == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- Filters ---

    private void OnFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag) { SelectFilter(tag); Refresh(); }
    }

    private void SelectFilter(string filter)
    {
        _filter = filter;
        foreach (var (btn, tag) in new[] { (FilterAll, "All"), (FilterActive, "Active"), (FilterPaused, "Paused") })
        {
            bool sel = tag == filter;
            btn.Background = sel ? new SolidColorBrush(Colors.White) : Transparent();
            btn.Foreground = sel ? Res("AccentBrush") : Res("TextSecondaryBrush");
        }
    }

    // --- Save (create / edit) ---

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryBuildAlert(out var alert, out var error))
        {
            FormError.Message = error;
            FormError.IsOpen = true;
            return;
        }

        Service.AddOrUpdate(alert);
        ResetForm();
    }

    private bool TryBuildAlert(out Alert alert, out string error)
    {
        alert = null!;
        error = "";

        string title = TitleInput.Text.Trim();
        if (string.IsNullOrEmpty(title)) { error = "Please enter a reminder title."; return false; }

        var now = DateTime.UtcNow;
        var tz = Service.TimeZone;
        var timeOfDay = TimeInput.SelectedTime ?? new TimeSpan(9, 0, 0);
        int minutes = (int)timeOfDay.TotalMinutes;

        var a = new Alert
        {
            Title = title,
            Message = string.IsNullOrWhiteSpace(DetailsInput.Text) ? null : DetailsInput.Text.Trim(),
            PlaySound = SoundInput.IsOn,
            TimeOfDayMinutes = minutes,
            IsEnabled = true,
            CreatedUtc = now,
        };

        switch (_routine)
        {
            case "Once":
                if (OnceDateInput.Date is not DateTimeOffset date)
                {
                    error = "Please pick a date for this one-time alert."; return false;
                }
                var onceLocal = DateTime.SpecifyKind(date.Date.Add(timeOfDay), DateTimeKind.Unspecified);
                if (ScheduleCalculator.SafeLocalToUtc(onceLocal, tz) <= now)
                {
                    error = "That date and time is in the past. Please choose a future time."; return false;
                }
                a.Repeat = RepeatType.Once;
                a.OnceLocal = onceLocal;
                break;

            case "Daily":
                a.Repeat = RepeatType.Daily;
                break;

            case "Weekly":
                var days = SelectedDays();
                if (days == WeekDays.None) { error = "Please select at least one day of the week."; return false; }
                a.Repeat = RepeatType.Weekly;
                a.Days = days;
                break;

            case "Custom":
                int count = (int)Math.Round(double.IsNaN(IntervalCountInput.Value) ? 1 : IntervalCountInput.Value);
                if (count < 1) { error = "The interval must be at least 1."; return false; }
                a.Repeat = RepeatType.Custom;
                a.IntervalCount = count;
                a.IntervalUnit = (IntervalUnit)Math.Max(0, IntervalUnitInput.SelectedIndex);
                a.AnchorUtc = now + ScheduleCalculator.IntervalToTimeSpan(a); // first fire one interval out
                break;
        }

        // Ends / limits
        switch (EndsInput.SelectedIndex)
        {
            case 1:
                if (EndDateInput.Date is not DateTimeOffset end) { error = "Please pick an end date."; return false; }
                a.EndDateLocal = DateTime.SpecifyKind(end.Date, DateTimeKind.Unspecified);
                break;
            case 2:
                int max = (int)Math.Round(double.IsNaN(EndCountInput.Value) ? 1 : EndCountInput.Value);
                if (max < 1) { error = "The number of times must be at least 1."; return false; }
                a.MaxOccurrences = max;
                break;
        }

        if (_editingId is Guid id)
        {
            a.Id = id;
            var existing = Service.Snapshot().FirstOrDefault(x => x.Id == id);
            if (existing is not null) a.CreatedUtc = existing.CreatedUtc;
        }

        alert = a;
        return true;
    }

    private WeekDays SelectedDays()
    {
        WeekDays d = WeekDays.None;
        if (DaySun.IsChecked == true) d |= WeekDays.Sunday;
        if (DayMon.IsChecked == true) d |= WeekDays.Monday;
        if (DayTue.IsChecked == true) d |= WeekDays.Tuesday;
        if (DayWed.IsChecked == true) d |= WeekDays.Wednesday;
        if (DayThu.IsChecked == true) d |= WeekDays.Thursday;
        if (DayFri.IsChecked == true) d |= WeekDays.Friday;
        if (DaySat.IsChecked == true) d |= WeekDays.Saturday;
        return d;
    }

    // --- Edit mode ---

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not Guid id) return;
        var a = Service.Snapshot().FirstOrDefault(x => x.Id == id);
        if (a is null) return;

        _editingId = id;
        FormTitle.Text = "Edit alert";
        SaveButton.Content = "Save changes";
        CancelEditButton.Visibility = Visibility.Visible;

        TitleInput.Text = a.Title;
        DetailsInput.Text = a.Message ?? "";
        SoundInput.IsOn = a.PlaySound;
        TimeInput.SelectedTime = TimeSpan.FromMinutes(a.TimeOfDayMinutes);

        SelectSegment(a.Repeat.ToString());

        // days
        SetDay(DaySun, a.Days.HasFlag(WeekDays.Sunday));
        SetDay(DayMon, a.Days.HasFlag(WeekDays.Monday));
        SetDay(DayTue, a.Days.HasFlag(WeekDays.Tuesday));
        SetDay(DayWed, a.Days.HasFlag(WeekDays.Wednesday));
        SetDay(DayThu, a.Days.HasFlag(WeekDays.Thursday));
        SetDay(DayFri, a.Days.HasFlag(WeekDays.Friday));
        SetDay(DaySat, a.Days.HasFlag(WeekDays.Saturday));

        if (a.Repeat == RepeatType.Once && a.OnceLocal is DateTime ol)
        {
            OnceDateInput.Date = new DateTimeOffset(ol.Date, TimeSpan.Zero);
            TimeInput.SelectedTime = ol.TimeOfDay;
        }
        if (a.Repeat == RepeatType.Custom)
        {
            IntervalCountInput.Value = a.IntervalCount;
            IntervalUnitInput.SelectedIndex = (int)a.IntervalUnit;
        }

        if (a.EndDateLocal is DateTime ed)
        {
            EndsInput.SelectedIndex = 1;
            EndDateInput.Date = new DateTimeOffset(ed.Date, TimeSpan.Zero);
        }
        else if (a.MaxOccurrences is int mx)
        {
            EndsInput.SelectedIndex = 2;
            EndCountInput.Value = mx;
        }
        else EndsInput.SelectedIndex = 0;

        FormCard.StartBringIntoView();
        TitleInput.Focus(FocusState.Programmatic);
    }

    private void SetDay(ToggleButton t, bool on)
    {
        t.IsChecked = on;
        OnDayToggled(t, new RoutedEventArgs());
    }

    private void OnCancelEditClick(object sender, RoutedEventArgs e) => ResetForm();

    private void ResetForm()
    {
        _editingId = null;
        FormTitle.Text = "Create an alert";
        SaveButton.Content = "Create alert";
        CancelEditButton.Visibility = Visibility.Collapsed;
        FormError.IsOpen = false;

        TitleInput.Text = "";
        DetailsInput.Text = "";
        SoundInput.IsOn = true;
        TimeInput.SelectedTime = new TimeSpan(9, 0, 0);
        OnceDateInput.Date = null;
        EndsInput.SelectedIndex = 0;
        IntervalCountInput.Value = 2;
        IntervalUnitInput.SelectedIndex = 1;
        foreach (var t in new[] { DaySun, DayMon, DayTue, DayWed, DayThu, DayFri, DaySat })
            SetDay(t, false);
        SelectSegment("Daily");
    }

    // --- List ---

    private void OnItemToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch ts || ts.Tag is not Guid id) return;
        var a = Service.Snapshot().FirstOrDefault(x => x.Id == id);
        if (a is null || a.IsEnabled == ts.IsOn) return; // guard against programmatic set
        Service.SetEnabled(id, ts.IsOn);
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not Guid id) return;
        var a = Service.Snapshot().FirstOrDefault(x => x.Id == id);
        if (a is null) return;

        _lastDeleted = a;
        Service.Delete(id);

        if (_editingId == id) ResetForm();

        UndoBar.Message = $"\"{a.Title}\" was removed.";
        UndoBar.IsOpen = true;
        _undoTimer?.Stop();
        _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _undoTimer.Tick += (_, _) => { _undoTimer!.Stop(); UndoBar.IsOpen = false; _lastDeleted = null; };
        _undoTimer.Start();
    }

    private void OnUndoClick(object sender, RoutedEventArgs e)
    {
        if (_lastDeleted is not null)
        {
            Service.Restore(_lastDeleted);
            _lastDeleted = null;
        }
        UndoBar.IsOpen = false;
        _undoTimer?.Stop();
    }

    private void Refresh()
    {
        var all = Service.Snapshot().Where(a => !a.IsCompleted);

        IEnumerable<Alert> filtered = _filter switch
        {
            "Active" => all.Where(a => a.IsEnabled),
            "Paused" => all.Where(a => !a.IsEnabled),
            _ => all,
        };

        var ordered = filtered
            .OrderBy(a => Service.NextFireUtc(a) ?? DateTime.MaxValue)
            .ThenBy(a => a.Title)
            .Select(a => AlertListItemViewModel.Create(a, Service, _use24))
            .ToList();

        AlertsList.ItemsSource = ordered;
        EmptyState.Visibility = ordered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static SolidColorBrush Transparent() => new(Colors.Transparent);
    private static SolidColorBrush Res(string key) => (SolidColorBrush)Application.Current.Resources[key];
}
