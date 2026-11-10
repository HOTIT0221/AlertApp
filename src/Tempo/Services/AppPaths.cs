namespace Tempo.Services;

/// <summary>
/// Resolves local data paths. By default data lives under <c>%LOCALAPPDATA%\Tempo</c>.
/// In <b>portable mode</b> — signalled by a <c>Tempo.portable</c> marker file next to the
/// executable — data is kept in a <c>Data</c> folder beside the executable instead, so the
/// whole app (and its reminders) can travel on a USB stick or any folder with no install.
/// </summary>
public static class AppPaths
{
    private static string? _dataDir;

    /// <summary>True when a <c>Tempo.portable</c> marker file sits next to the executable.</summary>
    public static bool IsPortable { get; } =
        File.Exists(Path.Combine(AppContext.BaseDirectory, "Tempo.portable"));

    public static string DataDirectory
    {
        get
        {
            if (_dataDir is not null) return _dataDir;

            string dir = IsPortable
                ? Path.Combine(AppContext.BaseDirectory, "Data")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tempo");

            try
            {
                Directory.CreateDirectory(dir);
            }
            catch
            {
                // Portable location isn't writable (e.g. read-only media): fall back to per-user data.
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tempo");
                Directory.CreateDirectory(dir);
            }

            _dataDir = dir;
            return dir;
        }
    }

    public static string DatabasePath => Path.Combine(DataDirectory, "tempo.db");

    public static string AssetsDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Assets");

    public static string AppIconPath => Path.Combine(AssetsDirectory, "Tempo.ico");
}
