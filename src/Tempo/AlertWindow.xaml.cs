using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tempo.Common;
using Tempo.Core;
using Tempo.Services;

namespace Tempo;

public sealed partial class AlertWindow : Window
{
    private readonly Guid _alertId;

    public event Action<Guid, int>? SnoozeRequested;
    public event Action<Guid>? DismissRequested;

    public AlertWindow(Alert alert, FireKind kind, int defaultSnoozeMinutes)
    {
        InitializeComponent();

        _alertId = alert.Id;
        Title = "Tempo — " + alert.Title;
        AppWindow.SetIcon(AppPaths.AppIconPath);

        TitleText.Text = string.IsNullOrWhiteSpace(alert.Title) ? "Reminder" : alert.Title;
        KindText.Text = kind == FireKind.Missed ? "MISSED REMINDER" : "REMINDER";
        if (!string.IsNullOrWhiteSpace(alert.Message))
            MessageText.Text = alert.Message;
        else
            MessageText.Visibility = Visibility.Collapsed;

        SnoozeCombo.SelectedIndex = defaultSnoozeMinutes switch
        {
            <= 5 => 0,
            <= 10 => 1,
            <= 30 => 2,
            _ => 3,
        };

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }
    }

    public void ShowCentered()
    {
        NativeWindow.CenterOnScreen(this, 540, 300);
        Activate();
        NativeWindow.BringToFront(this);
    }

    private void OnSnooze(object sender, RoutedEventArgs e)
    {
        int minutes = 10;
        if (SnoozeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag && int.TryParse(tag, out var m))
            minutes = m;
        SnoozeRequested?.Invoke(_alertId, minutes);
        Close();
    }

    private void OnDismiss(object sender, RoutedEventArgs e)
    {
        DismissRequested?.Invoke(_alertId);
        Close();
    }
}
