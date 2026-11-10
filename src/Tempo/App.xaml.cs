using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Tempo.Core;
using Tempo.Services;

namespace Tempo;

public partial class App : Application
{
    public static App? Instance { get; private set; }

    public DispatcherQueue Dispatcher { get; private set; } = null!;
    public AlertService Alerts { get; private set; } = null!;
    public NotificationService Notifications { get; private set; } = null!;

    private TempoDatabase _db = null!;
    private TrayIconService _tray = null!;
    private MainWindow? _mainWindow;
    private bool _isExiting;
    private readonly List<AlertWindow> _openAlertWindows = new();

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Error("App.UnhandledException", e.Exception);
            e.Handled = true; // keep running where possible; delivery continues
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            LaunchCore();
        }
        catch (Exception ex)
        {
            Log.Error("OnLaunched", ex);
            throw;
        }
    }

    private void LaunchCore()
    {
        Instance = this;
        Dispatcher = DispatcherQueue.GetForCurrentThread();

        _db = new TempoDatabase(AppPaths.DatabasePath);

        Alerts = new AlertService(_db, Dispatcher);
        Alerts.Load();
        Alerts.AlertFired += OnAlertFired;

        Notifications = new NotificationService();
        Notifications.Activated += OnNotificationActivated;
        Notifications.Register();

        _tray = new TrayIconService();
        _tray.OpenRequested += () => Dispatcher.TryEnqueue(ShowMainWindow);
        _tray.ExitRequested += () => Dispatcher.TryEnqueue(ExitApplication);
        _tray.Initialize("Tempo — reminders are running", AppPaths.AppIconPath);

        Alerts.Start();

        bool startHidden = Environment.GetCommandLineArgs()
            .Any(a => a.Equals("--startup", StringComparison.OrdinalIgnoreCase)
                   || a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

        _mainWindow = new MainWindow();
        _mainWindow.AppWindow.Closing += OnMainWindowClosing;

        if (!startHidden)
            ShowMainWindow();
    }

    // --- Window / tray behaviour ---

    public void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.AppWindow.Show();
        _mainWindow.Activate();
        _mainWindow.BringToFront();
    }

    public void OnRedirectedActivation()
    {
        Dispatcher.TryEnqueue(ShowMainWindow);
    }

    private void OnMainWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs e)
    {
        if (_isExiting) return;

        if (Alerts.Settings.CloseBehavior == CloseBehavior.MinimizeToTray)
        {
            e.Cancel = true;
            sender.Hide();
            ShowTrayNoticeIfFirstTime();
        }
        else
        {
            _isExiting = true;
            Dispatcher.TryEnqueue(ExitApplication);
        }
    }

    private void ShowTrayNoticeIfFirstTime()
    {
        if (Alerts.Settings.HasSeenTrayNotice) return;
        var updated = Alerts.Settings.Clone();
        updated.HasSeenTrayNotice = true;
        Alerts.UpdateSettings(updated);

        try
        {
            if (Notifications.IsAvailable)
            {
                var toast = new AppNotificationBuilder()
                    .AddArgument("action", "open")
                    .AddArgument("id", Guid.Empty.ToString())
                    .AddText("Tempo is still running")
                    .AddText("Your reminders stay active in the system tray. Right-click the tray icon to open or exit, or change this in Settings.")
                    .MuteAudio()
                    .BuildNotification();
                AppNotificationManager.Default.Show(toast);
            }
        }
        catch { /* best effort */ }
    }

    public void ExitApplication()
    {
        _isExiting = true;
        try { foreach (var w in _openAlertWindows.ToList()) w.Close(); } catch { }
        try { _tray.Dispose(); } catch { }
        try { Alerts.Dispose(); } catch { }
        try { Notifications.Dispose(); } catch { }
        try { _db.Dispose(); } catch { }
        try { _mainWindow?.Close(); } catch { }
        Exit();
    }

    // --- Delivery ---

    private void OnAlertFired(Alert alert, FireKind kind)
    {
        Dispatcher.TryEnqueue(() =>
        {
            bool mainVisible = _mainWindow?.AppWindow.IsVisible == true && _mainWindow.IsForeground;

            if (mainVisible)
            {
                // In the foreground: show the polished in-app alert instead of a background toast.
                ShowInAppAlert(alert, kind);
            }
            else
            {
                bool shown = Notifications.Show(alert, kind,
                    Alerts.Settings.SoundEnabled, Alerts.Settings.DefaultSnoozeMinutes);
                if (!shown)
                    ShowInAppAlert(alert, kind); // fallback when toasts are unavailable
            }
        });
    }

    private void ShowInAppAlert(Alert alert, FireKind kind)
    {
        if (Alerts.Settings.SoundEnabled && alert.PlaySound)
            SoundService.PlayGentle();

        var window = new AlertWindow(alert, kind, Alerts.Settings.DefaultSnoozeMinutes);
        window.SnoozeRequested += (id, minutes) => Alerts.Snooze(id, minutes);
        window.DismissRequested += id => Alerts.Dismiss(id);
        window.Closed += (_, _) => _openAlertWindows.Remove(window);
        _openAlertWindows.Add(window);
        window.ShowCentered();
    }

    private void OnNotificationActivated(NotificationActivation activation)
    {
        Dispatcher.TryEnqueue(() =>
        {
            switch (activation.Action)
            {
                case NotificationAction.Snooze:
                    Alerts.Snooze(activation.AlertId, activation.SnoozeMinutes);
                    break;
                case NotificationAction.Dismiss:
                    Alerts.Dismiss(activation.AlertId);
                    break;
                case NotificationAction.Open:
                    ShowMainWindow();
                    break;
            }
        });
    }
}
