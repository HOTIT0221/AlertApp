using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using Tempo.Core;

namespace Tempo.Services;

/// <summary>
/// Central store and in-process scheduler for alerts. Owns the single source of truth, persists
/// changes to SQLite, evaluates the firing engine on a timer, and raises delivery/collection events.
/// A single instance of this type exists per running Tempo process (duplicate processes are prevented
/// at the app level) so alerts are never scheduled twice.
/// </summary>
public sealed class AlertService : IDisposable
{
    private readonly TempoDatabase _db;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _timer;
    private readonly List<Alert> _alerts = new();
    private readonly object _lock = new();

    public AppSettings Settings { get; private set; } = new();

    /// <summary>The time zone used for calendar schedules; re-read when the system reports a change.</summary>
    public TimeZoneInfo TimeZone { get; private set; } = TimeZoneInfo.Local;

    public event Action? AlertsChanged;
    public event Action? SettingsChanged;
    public event Action<Alert, FireKind>? AlertFired;

    public AlertService(TempoDatabase db, DispatcherQueue dispatcher)
    {
        _db = db;
        _dispatcher = dispatcher;
        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => Tick();
    }

    public void Load()
    {
        lock (_lock)
        {
            _alerts.Clear();
            _alerts.AddRange(_db.GetAllAlerts());
            Settings = AppSettings.FromMap(_db.GetAllSettings());
        }
    }

    public void Start()
    {
        SystemEvents.TimeChanged += OnSystemTimeChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Tick();            // immediate catch-up for anything due or missed while not running
        _timer.Start();
    }

    private void OnSystemTimeChanged(object? sender, EventArgs e) =>
        _dispatcher.TryEnqueue(() => { TimeZoneInfo.ClearCachedData(); TimeZone = TimeZoneInfo.Local; Tick(); });

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            _dispatcher.TryEnqueue(Tick);
    }

    // --- scheduling ---

    private void Tick()
    {
        var now = DateTime.UtcNow;
        var tz = TimeZone;
        var policy = Settings.MissedReminderPolicy;

        var deliveries = new List<(Alert, FireKind)>();
        bool collectionChanged = false;

        List<Alert> snapshot;
        lock (_lock) snapshot = _alerts.ToList();

        foreach (var alert in snapshot)
        {
            var before = Signature(alert);
            var decision = FiringEngine.Evaluate(alert, now, tz, policy);
            if (!Signature(alert).Equals(before))
            {
                _db.UpsertAlert(alert);
                collectionChanged = true;
            }
            if (decision.ShouldDeliver)
                deliveries.Add((alert, decision.Kind));
        }

        if (collectionChanged)
            AlertsChanged?.Invoke();

        foreach (var (alert, kind) in deliveries)
            AlertFired?.Invoke(alert, kind);
    }

    private static (DateTime?, int, bool, DateTime?) Signature(Alert a) =>
        (a.LastFiredOccurrenceUtc, a.OccurrenceCount, a.IsCompleted, a.SnoozeUntilUtc);

    // --- queries ---

    public List<Alert> Snapshot()
    {
        lock (_lock) return _alerts.Select(a => a.Clone()).ToList();
    }

    public DateTime? NextFireUtc(Alert a) =>
        ScheduleCalculator.GetNextFireUtc(a, TimeZone, DateTime.UtcNow);

    // --- commands ---

    public void AddOrUpdate(Alert alert)
    {
        lock (_lock)
        {
            alert.UpdatedUtc = DateTime.UtcNow;
            var idx = _alerts.FindIndex(a => a.Id == alert.Id);
            if (idx >= 0) _alerts[idx] = alert;
            else _alerts.Add(alert);
            _db.UpsertAlert(alert);
        }
        AlertsChanged?.Invoke();
        PokeSoon();
    }

    public void Delete(Guid id)
    {
        lock (_lock)
        {
            _alerts.RemoveAll(a => a.Id == id);
            _db.DeleteAlert(id);
        }
        AlertsChanged?.Invoke();
    }

    public void Restore(Alert alert)
    {
        lock (_lock)
        {
            if (!_alerts.Any(a => a.Id == alert.Id))
                _alerts.Add(alert);
            _db.UpsertAlert(alert);
        }
        AlertsChanged?.Invoke();
        PokeSoon();
    }

    public void SetEnabled(Guid id, bool enabled)
    {
        Alert? found = Find(id);
        if (found is null) return;
        found.IsEnabled = enabled;
        if (enabled) found.IsCompleted = false; // resuming re-arms the schedule
        found.SnoozeUntilUtc = enabled ? found.SnoozeUntilUtc : null;
        found.UpdatedUtc = DateTime.UtcNow;
        Persist(found);
        AlertsChanged?.Invoke();
        PokeSoon();
    }

    public void Snooze(Guid id, int minutes)
    {
        var a = Find(id);
        if (a is null) return;
        FiringEngine.Snooze(a, DateTime.UtcNow, minutes);
        Persist(a);
        AlertsChanged?.Invoke();
    }

    public void Dismiss(Guid id)
    {
        var a = Find(id);
        if (a is null) return;
        FiringEngine.Dismiss(a, DateTime.UtcNow);
        Persist(a);
        AlertsChanged?.Invoke();
    }

    private Alert? Find(Guid id)
    {
        lock (_lock) return _alerts.FirstOrDefault(a => a.Id == id);
    }

    private void Persist(Alert a)
    {
        lock (_lock) _db.UpsertAlert(a);
    }

    private void PokeSoon() => _dispatcher.TryEnqueue(Tick);

    // --- settings ---

    public void UpdateSettings(AppSettings updated)
    {
        lock (_lock)
        {
            Settings = updated.Clone();
            foreach (var (k, v) in Settings.ToMap())
                _db.SetSetting(k, v);
        }
        SettingsChanged?.Invoke();
    }

    public void Dispose()
    {
        _timer.Stop();
        SystemEvents.TimeChanged -= OnSystemTimeChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
