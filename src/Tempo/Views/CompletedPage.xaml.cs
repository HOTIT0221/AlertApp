using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tempo.Core;
using Tempo.Services;
using Tempo.ViewModels;

namespace Tempo.Views;

public sealed partial class CompletedPage : Page
{
    private AlertService Service => App.Instance!.Alerts;
    private bool _use24;

    public CompletedPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _use24 = ClockFormatHelper.Use24Hour(Service.Settings.ClockFormat);
            Service.AlertsChanged += Refresh;
            Refresh();
        };
        Unloaded += (_, _) => Service.AlertsChanged -= Refresh;
    }

    private void Refresh()
    {
        var items = Service.Snapshot()
            .Where(a => a.IsCompleted)
            .OrderByDescending(a => a.LastFiredUtc ?? a.UpdatedUtc)
            .Select(a => AlertListItemViewModel.Create(a, Service, _use24))
            .ToList();

        List.ItemsSource = items;
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearAllButton.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnReactivate(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not Guid id) return;
        var a = Service.Snapshot().FirstOrDefault(x => x.Id == id);
        if (a is null) return;

        a.IsCompleted = false;
        a.IsEnabled = true;
        a.OccurrenceCount = 0;
        a.LastFiredUtc = null;
        a.LastFiredOccurrenceUtc = null;
        a.SnoozeUntilUtc = null;

        // For a one-time alert whose moment has passed, re-anchor it a minute out so it has a future occurrence.
        if (a.Repeat == RepeatType.Once)
        {
            var nextLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow.AddMinutes(1), Service.TimeZone);
            a.OnceLocal = DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified);
        }
        else if (a.Repeat == RepeatType.Custom)
        {
            a.AnchorUtc = DateTime.UtcNow + ScheduleCalculator.IntervalToTimeSpan(a);
        }

        Service.AddOrUpdate(a);
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is Guid id)
            Service.Delete(id);
    }

    private async void OnClearAll(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Clear completed alerts?",
            Content = "This permanently removes all completed reminders. This can't be undone.",
            PrimaryButtonText = "Clear all",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            foreach (var a in Service.Snapshot().Where(x => x.IsCompleted))
                Service.Delete(a.Id);
        }
    }
}
