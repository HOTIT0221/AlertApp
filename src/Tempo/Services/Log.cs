namespace Tempo.Services;

/// <summary>Minimal startup/crash logger written to %LOCALAPPDATA%\Tempo\tempo.log.</summary>
public static class Log
{
    private static readonly object Gate = new();

    private static string LogPath => Path.Combine(AppPaths.DataDirectory, "tempo.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch { /* logging must never throw */ }
    }

    public static void Error(string context, Exception ex) =>
        Write($"ERROR [{context}]: {ex.GetType().Name}: {ex.Message} (HRESULT=0x{ex.HResult:X8})"
            + $"{Environment.NewLine}Inner: {ex.InnerException?.ToString() ?? "(none)"}{Environment.NewLine}{ex}");
}
