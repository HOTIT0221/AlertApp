using System.Globalization;
using Tempo.Core;

namespace Tempo.Services;

/// <summary>Resolves whether to present 12- or 24-hour times based on settings / Windows preference.</summary>
public static class ClockFormatHelper
{
    /// <summary>
    /// Returns true when times should be shown in 24-hour format. When the setting is
    /// <see cref="ClockFormat.System"/>, the current Windows short-time pattern is inspected.
    /// </summary>
    public static bool Use24Hour(ClockFormat format)
    {
        return format switch
        {
            ClockFormat.Hours24 => true,
            ClockFormat.Hours12 => false,
            _ => SystemPrefers24Hour(),
        };
    }

    private static bool SystemPrefers24Hour()
    {
        // The short time pattern contains "H" (capital) for 24-hour clocks and "h" for 12-hour.
        var pattern = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern;
        return pattern.Contains('H');
    }
}
