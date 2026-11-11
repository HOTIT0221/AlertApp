using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Tempo.Core;

namespace Tempo.Services;

public enum NotificationAction { Open, Snooze, Dismiss }

public sealed record NotificationActivation(NotificationAction Action, Guid AlertId, int SnoozeMinutes);

/// <summary>
/// Delivers Windows toast notifications with Snooze / Dismiss actions via the Windows App SDK
/// (<see cref="AppNotificationManager"/>), supporting unpackaged activation. Falls back gracefully
/// when notifications are unavailable.
/// </summary>
public sealed class NotificationService : IDisposable
{
    private bool _registered;

    /// <summary>Raised when the user interacts with a toast (may arrive on a background thread).</summary>
    public event Action<NotificationActivation>? Activated;

    /// <summary>True if toast registration succeeded and notifications can be shown.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Set when registration failed, for surfacing a clear message to the user.</summary>
    public string? LastError { get; private set; }

    public void Register()
    {
        try
        {
            var mgr = AppNotificationManager.Default;
            mgr.NotificationInvoked += OnNotificationInvoked;
            mgr.Register();
            _registered = true;
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            LastError = ex.Message;
        }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        try
        {
            var a = args.Arguments;
            a.TryGetValue("action", out var action);
            a.TryGetValue("id", out var idStr);
            if (!Guid.TryParse(idStr, out var id)) return;

            int minutes = 10;
            if (args.UserInput.TryGetValue("snoozeMinutes", out var mins) && int.TryParse(mins, out var m))
                minutes = m;

            var parsed = action switch
            {
                "snooze" => NotificationAction.Snooze,
                "dismiss" => NotificationAction.Dismiss,
                _ => NotificationAction.Open,
            };
            Activated?.Invoke(new NotificationActivation(parsed, id, minutes));
        }
        catch
        {
            // Never let a malformed activation crash the handler.
        }
    }

    /// <summary>
    /// Shows a toast for an alert. Returns false if the toast could not be shown (caller should
    /// fall back to the in-app alert window).
    /// </summary>
    public bool Show(Alert alert, FireKind kind, bool soundEnabledGlobally, int defaultSnoozeMinutes)
    {
        if (!IsAvailable) return false;

        try
        {
            string title = alert.Title;
            if (kind == FireKind.Missed)
                title = alert.Title + "  ·  missed while away";

            var builder = new AppNotificationBuilder()
                .AddArgument("id", alert.Id.ToString())
                .AddArgument("action", "open")
                // Reminder keeps the toast on screen until the user acts on it (snooze/dismiss),
                // instead of auto-dismissing into the Action Center after a few seconds.
                .SetScenario(AppNotificationScenario.Reminder)
                .AddText(title);

            if (!string.IsNullOrWhiteSpace(alert.Message))
                builder.AddText(alert.Message);

            builder.AddText("Tempo");

            int def = Math.Clamp(defaultSnoozeMinutes, 1, 1440);
            var combo = new AppNotificationComboBox("snoozeMinutes")
                .AddItem("5", "5 minutes")
                .AddItem("10", "10 minutes")
                .AddItem("30", "30 minutes")
                .AddItem("60", "1 hour")
                .SetSelectedItem(SnoozeKey(def));
            builder.AddComboBox(combo);

            builder.AddButton(new AppNotificationButton("Snooze")
                .AddArgument("action", "snooze")
                .AddArgument("id", alert.Id.ToString()));
            builder.AddButton(new AppNotificationButton("Dismiss")
                .AddArgument("action", "dismiss")
                .AddArgument("id", alert.Id.ToString()));

            if (!soundEnabledGlobally || !alert.PlaySound)
                builder.MuteAudio();

            var notification = builder.BuildNotification();
            notification.ExpiresOnReboot = true;
            AppNotificationManager.Default.Show(notification);
            return notification.Id != 0;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private static string SnoozeKey(int minutes) => minutes switch
    {
        <= 5 => "5",
        <= 10 => "10",
        <= 30 => "30",
        _ => "60",
    };

    public void Dispose()
    {
        try
        {
            if (_registered)
                AppNotificationManager.Default.Unregister();
        }
        catch { /* ignore */ }
    }
}
