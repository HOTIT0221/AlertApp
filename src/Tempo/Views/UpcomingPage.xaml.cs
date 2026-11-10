using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tempo.Core;
using Tempo.Services;
using Tempo.ViewModels;

namespace Tempo.Views;

public sealed partial class UpcomingPage : Page
{
    private AlertService Service => App.Instance!.Alerts;
    private bool _use24;

    public UpcomingPage()
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
            .Where(a => a.IsEnabled && !a.IsCompleted && Service.NextFireUtc(a) is not null)
            .OrderBy(a => Service.NextFireUtc(a) ?? DateTime.MaxValue)
            .Select(a => AlertListItemViewModel.Create(a, Service, _use24))
            .ToList();

        List.ItemsSource = items;
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnItemToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch ts || ts.Tag is not Guid id) return;
        var a = Service.Snapshot().FirstOrDefault(x => x.Id == id);
        if (a is null || a.IsEnabled == ts.IsOn) return;
        Service.SetEnabled(id, ts.IsOn);
    }
}
