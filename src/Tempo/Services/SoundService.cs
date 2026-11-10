using System.Runtime.InteropServices;

namespace Tempo.Services;

/// <summary>Plays a subtle alert sound for the in-app alert window (toasts use their own audio).</summary>
public static class SoundService
{
    // MB_OK plays the system "Default Beep" (SystemAsterisk-style) sound, respecting the user's sound scheme.
    private const uint MB_OK = 0x00000000;

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);

    public static void PlayGentle()
    {
        try
        {
            MessageBeep(MB_OK);
        }
        catch
        {
            // Audio is best-effort; never let it interrupt delivery.
        }
    }
}
