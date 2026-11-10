using Tempo.Core;
using Xunit;

namespace Tempo.Core.Tests;

public class PersistenceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tempo-test-{Guid.NewGuid():N}.db");

    [Fact]
    public void Alert_RoundTrips_AllFields()
    {
        var original = new Alert
        {
            Title = "Stand up",
            Message = "Stretch for a minute",
            IsEnabled = true,
            PlaySound = true,
            Repeat = RepeatType.Weekly,
            TimeOfDayMinutes = 14 * 60 + 30,
            Days = WeekDays.Monday | WeekDays.Wednesday | WeekDays.Friday,
            OnceLocal = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Unspecified),
            IntervalCount = 3,
            IntervalUnit = IntervalUnit.Days,
            AnchorUtc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            EndDateLocal = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Unspecified),
            MaxOccurrences = 10,
            OccurrenceCount = 2,
            LastFiredUtc = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc),
            SnoozeUntilUtc = new DateTime(2026, 6, 3, 12, 0, 0, DateTimeKind.Utc),
        };

        using (var db = new TempoDatabase(_dbPath))
            db.UpsertAlert(original);

        using (var db = new TempoDatabase(_dbPath))
        {
            var all = db.GetAllAlerts();
            var a = Assert.Single(all);

            Assert.Equal(original.Id, a.Id);
            Assert.Equal("Stand up", a.Title);
            Assert.Equal("Stretch for a minute", a.Message);
            Assert.Equal(RepeatType.Weekly, a.Repeat);
            Assert.Equal(14 * 60 + 30, a.TimeOfDayMinutes);
            Assert.Equal(WeekDays.Monday | WeekDays.Wednesday | WeekDays.Friday, a.Days);
            Assert.Equal(original.OnceLocal, a.OnceLocal);
            Assert.Equal(DateTimeKind.Unspecified, a.OnceLocal!.Value.Kind);
            Assert.Equal(IntervalUnit.Days, a.IntervalUnit);
            Assert.Equal(3, a.IntervalCount);
            Assert.Equal(original.AnchorUtc, a.AnchorUtc);
            Assert.Equal(original.EndDateLocal, a.EndDateLocal);
            Assert.Equal(10, a.MaxOccurrences);
            Assert.Equal(2, a.OccurrenceCount);
            Assert.Equal(original.LastFiredUtc, a.LastFiredUtc);
            Assert.Equal(original.SnoozeUntilUtc, a.SnoozeUntilUtc);
        }
    }

    [Fact]
    public void Upsert_Updates_ExistingAlert()
    {
        var a = new Alert { Title = "First", Repeat = RepeatType.Daily, TimeOfDayMinutes = 540 };
        using var db = new TempoDatabase(_dbPath);
        db.UpsertAlert(a);

        a.Title = "Edited";
        a.IsEnabled = false;
        db.UpsertAlert(a);

        var reloaded = Assert.Single(db.GetAllAlerts());
        Assert.Equal("Edited", reloaded.Title);
        Assert.False(reloaded.IsEnabled);
    }

    [Fact]
    public void Delete_RemovesAlert()
    {
        var a = new Alert { Title = "Temp", Repeat = RepeatType.Daily };
        using var db = new TempoDatabase(_dbPath);
        db.UpsertAlert(a);
        db.DeleteAlert(a.Id);
        Assert.Empty(db.GetAllAlerts());
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        using var db = new TempoDatabase(_dbPath);
        var s = new AppSettings { CloseBehavior = CloseBehavior.Exit, DefaultSnoozeMinutes = 15, StartWithWindows = true };
        foreach (var (k, v) in s.ToMap()) db.SetSetting(k, v);

        var loaded = AppSettings.FromMap(db.GetAllSettings());
        Assert.Equal(CloseBehavior.Exit, loaded.CloseBehavior);
        Assert.Equal(15, loaded.DefaultSnoozeMinutes);
        Assert.True(loaded.StartWithWindows);
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* ignore */ }
        foreach (var ext in new[] { "-wal", "-shm" })
            try { File.Delete(_dbPath + ext); } catch { /* ignore */ }
    }
}
