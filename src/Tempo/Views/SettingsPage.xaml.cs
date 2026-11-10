using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tempo.Core;
using Tempo.Services;

namespace Tempo.Views;

public sealed partial class SettingsPage : Page
{
    private AlertService Service => App.Instance!.Alerts;
    // True from construction so control events raised during InitializeComponent
    // (e.g. NumberBox Value set in XAML) don't trigger a premature Save.
    private bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        var s = Service.Settings;

        CloseBehaviorInput.SelectedIndex = s.CloseBehavior == CloseBehavior.Exit ? 1 : 0;
        ClockInput.SelectedIndex = (int)s.ClockFormat;
        MissedInput.SelectedIndex = (int)s.MissedReminderPolicy;
        SnoozeInput.Value = s.DefaultSnoozeMinutes;
        SoundInput.IsOn = s.SoundEnabled;
        StartupInput.IsOn = StartupService.IsEnabled();

        if (App.Instance!.Notifications.IsAvailable)
        {
            NotificationInfo.IsOpen = false;
        }
        else
        {
            NotificationInfo.Severity = InfoBarSeverity.Warning;
            NotificationInfo.Title = "Windows notifications are unavailable";
            NotificationInfo.Message = "Tempo will show its own in-app alert window instead. "
                + "You can enable notifications for Tempo in Windows Settings › System › Notifications.";
            NotificationInfo.IsOpen = true;
        }

        DataPathText.Text = (AppPaths.IsPortable ? "Portable mode — reminders are stored beside the app at: " : "Your reminders are stored locally at: ")
            + AppPaths.DatabasePath;

        _loading = false;
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e) => Save();

    private void OnSnoozeChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_loading) Save();
    }

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool ok = StartupService.SetEnabled(StartupInput.IsOn);
        if (!ok)
        {
            // Reflect the real state if the registry write failed.
            _loading = true;
            StartupInput.IsOn = StartupService.IsEnabled();
            _loading = false;
        }
        Save();
    }

    private void Save()
    {
        if (_loading) return;
        var s = Service.Settings.Clone();
        s.CloseBehavior = CloseBehaviorInput.SelectedIndex == 1 ? CloseBehavior.Exit : CloseBehavior.MinimizeToTray;
        s.ClockFormat = (ClockFormat)Math.Max(0, ClockInput.SelectedIndex);
        s.MissedReminderPolicy = (MissedReminderPolicy)Math.Max(0, MissedInput.SelectedIndex);
        s.SoundEnabled = SoundInput.IsOn;
        s.StartWithWindows = StartupInput.IsOn;
        double v = SnoozeInput.Value;
        s.DefaultSnoozeMinutes = double.IsNaN(v) ? 10 : Math.Clamp((int)v, 1, 1440);
        Service.UpdateSettings(s);
    }
}
