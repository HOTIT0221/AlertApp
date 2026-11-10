using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Tempo.Core;

/// <summary>
/// SQLite-backed persistence for alerts and settings. Thread-safe via a single guarded connection,
/// which is appropriate for a desktop app where the scheduler runs on a background timer.
/// </summary>
public sealed class TempoDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    public TempoDatabase(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString());
        _connection.Open();

        Execute("PRAGMA journal_mode=WAL;");
        Execute("PRAGMA foreign_keys=ON;");
        CreateSchema();
    }

    private void CreateSchema()
    {
        Execute("""
            CREATE TABLE IF NOT EXISTS alerts (
                Id TEXT PRIMARY KEY,
                Title TEXT NOT NULL,
                Message TEXT NULL,
                IsEnabled INTEGER NOT NULL,
                IsCompleted INTEGER NOT NULL,
                PlaySound INTEGER NOT NULL,
                Repeat INTEGER NOT NULL,
                TimeOfDayMinutes INTEGER NOT NULL,
                Days INTEGER NOT NULL,
                OnceLocal TEXT NULL,
                IntervalCount INTEGER NOT NULL,
                IntervalUnit INTEGER NOT NULL,
                AnchorUtc TEXT NOT NULL,
                EndDateLocal TEXT NULL,
                MaxOccurrences INTEGER NULL,
                OccurrenceCount INTEGER NOT NULL,
                LastFiredUtc TEXT NULL,
                LastFiredOccurrenceUtc TEXT NULL,
                SnoozeUntilUtc TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            """);

        Execute("""
            CREATE TABLE IF NOT EXISTS settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """);
    }

    // --- Alerts ---

    public List<Alert> GetAllAlerts()
    {
        lock (_gate)
        {
            var list = new List<Alert>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM alerts;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(ReadAlert(reader));
            return list;
        }
    }

    public void UpsertAlert(Alert a)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO alerts (Id,Title,Message,IsEnabled,IsCompleted,PlaySound,Repeat,TimeOfDayMinutes,
                    Days,OnceLocal,IntervalCount,IntervalUnit,AnchorUtc,EndDateLocal,MaxOccurrences,
                    OccurrenceCount,LastFiredUtc,LastFiredOccurrenceUtc,SnoozeUntilUtc,CreatedUtc,UpdatedUtc)
                VALUES ($Id,$Title,$Message,$IsEnabled,$IsCompleted,$PlaySound,$Repeat,$TimeOfDayMinutes,
                    $Days,$OnceLocal,$IntervalCount,$IntervalUnit,$AnchorUtc,$EndDateLocal,$MaxOccurrences,
                    $OccurrenceCount,$LastFiredUtc,$LastFiredOccurrenceUtc,$SnoozeUntilUtc,$CreatedUtc,$UpdatedUtc)
                ON CONFLICT(Id) DO UPDATE SET
                    Title=excluded.Title, Message=excluded.Message, IsEnabled=excluded.IsEnabled,
                    IsCompleted=excluded.IsCompleted, PlaySound=excluded.PlaySound, Repeat=excluded.Repeat,
                    TimeOfDayMinutes=excluded.TimeOfDayMinutes, Days=excluded.Days, OnceLocal=excluded.OnceLocal,
                    IntervalCount=excluded.IntervalCount, IntervalUnit=excluded.IntervalUnit,
                    AnchorUtc=excluded.AnchorUtc, EndDateLocal=excluded.EndDateLocal,
                    MaxOccurrences=excluded.MaxOccurrences, OccurrenceCount=excluded.OccurrenceCount,
                    LastFiredUtc=excluded.LastFiredUtc, LastFiredOccurrenceUtc=excluded.LastFiredOccurrenceUtc,
                    SnoozeUntilUtc=excluded.SnoozeUntilUtc, CreatedUtc=excluded.CreatedUtc, UpdatedUtc=excluded.UpdatedUtc;
                """;
            BindAlert(cmd, a);
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteAlert(Guid id)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM alerts WHERE Id=$Id;";
            cmd.Parameters.AddWithValue("$Id", id.ToString());
            cmd.ExecuteNonQuery();
        }
    }

    // --- Settings ---

    public Dictionary<string, string> GetAllSettings()
    {
        lock (_gate)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Key, Value FROM settings;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                map[reader.GetString(0)] = reader.GetString(1);
            return map;
        }
    }

    public void SetSetting(string key, string value)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO settings (Key, Value) VALUES ($Key, $Value)
                ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;
                """;
            cmd.Parameters.AddWithValue("$Key", key);
            cmd.Parameters.AddWithValue("$Value", value);
            cmd.ExecuteNonQuery();
        }
    }

    // --- mapping helpers ---

    private static void BindAlert(SqliteCommand cmd, Alert a)
    {
        cmd.Parameters.AddWithValue("$Id", a.Id.ToString());
        cmd.Parameters.AddWithValue("$Title", a.Title);
        cmd.Parameters.AddWithValue("$Message", (object?)a.Message ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$IsEnabled", a.IsEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$IsCompleted", a.IsCompleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$PlaySound", a.PlaySound ? 1 : 0);
        cmd.Parameters.AddWithValue("$Repeat", (int)a.Repeat);
        cmd.Parameters.AddWithValue("$TimeOfDayMinutes", a.TimeOfDayMinutes);
        cmd.Parameters.AddWithValue("$Days", (int)a.Days);
        cmd.Parameters.AddWithValue("$OnceLocal", (object?)LocalToString(a.OnceLocal) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$IntervalCount", a.IntervalCount);
        cmd.Parameters.AddWithValue("$IntervalUnit", (int)a.IntervalUnit);
        cmd.Parameters.AddWithValue("$AnchorUtc", UtcToString(a.AnchorUtc));
        cmd.Parameters.AddWithValue("$EndDateLocal", (object?)LocalToString(a.EndDateLocal) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$MaxOccurrences", (object?)a.MaxOccurrences ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$OccurrenceCount", a.OccurrenceCount);
        cmd.Parameters.AddWithValue("$LastFiredUtc", (object?)UtcToStringN(a.LastFiredUtc) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$LastFiredOccurrenceUtc", (object?)UtcToStringN(a.LastFiredOccurrenceUtc) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$SnoozeUntilUtc", (object?)UtcToStringN(a.SnoozeUntilUtc) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$CreatedUtc", UtcToString(a.CreatedUtc));
        cmd.Parameters.AddWithValue("$UpdatedUtc", UtcToString(a.UpdatedUtc));
    }

    private static Alert ReadAlert(SqliteDataReader r)
    {
        return new Alert
        {
            Id = Guid.Parse(GetString(r, "Id")),
            Title = GetString(r, "Title"),
            Message = GetStringN(r, "Message"),
            IsEnabled = GetInt(r, "IsEnabled") != 0,
            IsCompleted = GetInt(r, "IsCompleted") != 0,
            PlaySound = GetInt(r, "PlaySound") != 0,
            Repeat = (RepeatType)GetInt(r, "Repeat"),
            TimeOfDayMinutes = GetInt(r, "TimeOfDayMinutes"),
            Days = (WeekDays)GetInt(r, "Days"),
            OnceLocal = ParseLocalN(GetStringN(r, "OnceLocal")),
            IntervalCount = GetInt(r, "IntervalCount"),
            IntervalUnit = (IntervalUnit)GetInt(r, "IntervalUnit"),
            AnchorUtc = ParseUtc(GetString(r, "AnchorUtc")),
            EndDateLocal = ParseLocalN(GetStringN(r, "EndDateLocal")),
            MaxOccurrences = GetIntN(r, "MaxOccurrences"),
            OccurrenceCount = GetInt(r, "OccurrenceCount"),
            LastFiredUtc = ParseUtcN(GetStringN(r, "LastFiredUtc")),
            LastFiredOccurrenceUtc = ParseUtcN(GetStringN(r, "LastFiredOccurrenceUtc")),
            SnoozeUntilUtc = ParseUtcN(GetStringN(r, "SnoozeUntilUtc")),
            CreatedUtc = ParseUtc(GetString(r, "CreatedUtc")),
            UpdatedUtc = ParseUtc(GetString(r, "UpdatedUtc")),
        };
    }

    private const string LocalFormat = "yyyy-MM-ddTHH:mm:ss";

    private static string UtcToString(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    private static string? UtcToStringN(DateTime? utc) => utc.HasValue ? UtcToString(utc.Value) : null;

    private static DateTime ParseUtc(string s) =>
        DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
    private static DateTime? ParseUtcN(string? s) => string.IsNullOrEmpty(s) ? null : ParseUtc(s);

    private static string? LocalToString(DateTime? local) =>
        local.HasValue ? local.Value.ToString(LocalFormat, CultureInfo.InvariantCulture) : null;
    private static DateTime? ParseLocalN(string? s) =>
        string.IsNullOrEmpty(s) ? null
        : DateTime.SpecifyKind(DateTime.ParseExact(s, LocalFormat, CultureInfo.InvariantCulture), DateTimeKind.Unspecified);

    private static string GetString(SqliteDataReader r, string col) => r.GetString(r.GetOrdinal(col));
    private static string? GetStringN(SqliteDataReader r, string col)
    {
        int i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }
    private static int GetInt(SqliteDataReader r, string col) => r.GetInt32(r.GetOrdinal(col));
    private static int? GetIntN(SqliteDataReader r, string col)
    {
        int i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetInt32(i);
    }

    private void Execute(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
