using System.Globalization;

namespace Tempo.Core;

/// <summary>Strongly-typed application settings, persisted as key/value rows.</summary>
public sealed class AppSettings
{
    public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.MinimizeToTray;
    public bool StartWithWindows { get; set; } = false;
    public ClockFormat ClockFormat { get; set; } = ClockFormat.System;
    public MissedReminderPolicy MissedReminderPolicy { get; set; } = MissedReminderPolicy.ShowOnce;
    public int DefaultSnoozeMinutes { get; set; } = 10;
    public bool SoundEnabled { get; set; } = true;

    /// <summary>Whether the user has been shown the "Tempo keeps running in the tray" first-time notice.</summary>
    public bool HasSeenTrayNotice { get; set; } = false;

    public static AppSettings FromMap(IReadOnlyDictionary<string, string> map)
    {
        var s = new AppSettings();
        if (TryGet(map, nameof(CloseBehavior), out var cb) && Enum.TryParse<CloseBehavior>(cb, out var cbv)) s.CloseBehavior = cbv;
        if (TryGet(map, nameof(StartWithWindows), out var sw)) s.StartWithWindows = ParseBool(sw);
        if (TryGet(map, nameof(ClockFormat), out var cf) && Enum.TryParse<ClockFormat>(cf, out var cfv)) s.ClockFormat = cfv;
        if (TryGet(map, nameof(MissedReminderPolicy), out var mp) && Enum.TryParse<MissedReminderPolicy>(mp, out var mpv)) s.MissedReminderPolicy = mpv;
        if (TryGet(map, nameof(DefaultSnoozeMinutes), out var sn) && int.TryParse(sn, NumberStyles.Integer, CultureInfo.InvariantCulture, out var snv)) s.DefaultSnoozeMinutes = Math.Clamp(snv, 1, 1440);
        if (TryGet(map, nameof(SoundEnabled), out var se)) s.SoundEnabled = ParseBool(se);
        if (TryGet(map, nameof(HasSeenTrayNotice), out var tn)) s.HasSeenTrayNotice = ParseBool(tn);
        return s;
    }

    public Dictionary<string, string> ToMap() => new()
    {
        [nameof(CloseBehavior)] = CloseBehavior.ToString(),
        [nameof(StartWithWindows)] = StartWithWindows ? "1" : "0",
        [nameof(ClockFormat)] = ClockFormat.ToString(),
        [nameof(MissedReminderPolicy)] = MissedReminderPolicy.ToString(),
        [nameof(DefaultSnoozeMinutes)] = DefaultSnoozeMinutes.ToString(CultureInfo.InvariantCulture),
        [nameof(SoundEnabled)] = SoundEnabled ? "1" : "0",
        [nameof(HasSeenTrayNotice)] = HasSeenTrayNotice ? "1" : "0",
    };

    private static bool TryGet(IReadOnlyDictionary<string, string> map, string key, out string value)
        => map.TryGetValue(key, out value!);

    private static bool ParseBool(string s) => s is "1" or "true" or "True" or "yes";

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
