using System.Text.Json;
using Microsoft.Data.Sqlite;
using WorkLifeBalance.Application;
using WorkLifeBalance.Domain;

namespace WorkLifeBalance.Infrastructure;

public sealed partial class SqliteTrackerStore : ITrackerStore, IHistoryArchive, ITodoStore
{
    private readonly SqliteConnection connection;
    private readonly string databasePath;

    public SqliteTrackerStore(string path)
    {
        databasePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, DefaultTimeout = 3 }.ToString());
        try { connection.Open(); Initialize(path); }
        catch { connection.Dispose(); throw; }
    }

    private void Initialize(string path)
    {
        var version = Convert.ToInt32(Scalar("PRAGMA user_version;"));
        if (version > 4) throw new InvalidOperationException("This database was created by a newer application.");
        Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON;");
        if (version == 0)
        {
            using var transaction = connection.BeginTransaction();
            Execute("""
                CREATE TABLE settings (id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL);
                CREATE TABLE activity (id INTEGER PRIMARY KEY, start INTEGER NOT NULL, end INTEGER NOT NULL,
                    executable TEXT NOT NULL, category INTEGER NOT NULL, CHECK(end > start));
                CREATE INDEX activity_range ON activity(end, start);
                CREATE TABLE away (id INTEGER PRIMARY KEY, start INTEGER NOT NULL, end INTEGER NOT NULL,
                    reason TEXT NOT NULL, CHECK(end > start));
                CREATE INDEX away_range ON away(end, start);
                PRAGMA user_version=1;
                """, transaction);
            transaction.Commit();
        }
        if (version < 2)
        {
            if (version == 1)
            {
                using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path + ".before-v2.bak" }.ToString());
                backup.Open();
                connection.BackupDatabase(backup);
            }
            using var transaction = connection.BeginTransaction();
            Execute("""
                ALTER TABLE activity ADD COLUMN browser TEXT;
                CREATE TABLE corrections (id INTEGER PRIMARY KEY, action_id TEXT NOT NULL, start INTEGER NOT NULL,
                    end INTEGER NOT NULL, category INTEGER NOT NULL, created INTEGER NOT NULL,
                    undone INTEGER NOT NULL DEFAULT 0, CHECK(end > start));
                CREATE INDEX corrections_range ON corrections(undone,end,start);
                CREATE TABLE rule_revisions (id INTEGER PRIMARY KEY, until INTEGER NOT NULL, processes TEXT NOT NULL);
                PRAGMA user_version=2;
                """, transaction);
            transaction.Commit();
        }
        if (version < 3)
        {
            if (version > 0)
            {
                using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path + ".before-v3.bak" }.ToString());
                backup.Open();
                connection.BackupDatabase(backup);
            }
            using var transaction = connection.BeginTransaction();
            Execute("CREATE TABLE pauses (start INTEGER PRIMARY KEY, end INTEGER NOT NULL CHECK(end > start)); PRAGMA user_version=3;", transaction);
            transaction.Commit();
        }
        if (version < 4)
        {
            if (version > 0)
            {
                using var backup = Open(path + ".before-v4.bak", SqliteOpenMode.ReadWriteCreate);
                connection.BackupDatabase(backup);
            }
            using var transaction = connection.BeginTransaction();
            Execute("""
                CREATE TABLE todos (
                    id TEXT PRIMARY KEY, title TEXT NOT NULL CHECK(length(title) BETWEEN 1 AND 240),
                    weight INTEGER NOT NULL CHECK(weight BETWEEN 1 AND 5),
                    planned_day INTEGER NOT NULL CHECK(planned_day >= 719162 AND planned_day <= 3651693),
                    completed_at INTEGER, completed_day INTEGER,
                    CHECK((completed_at IS NULL AND completed_day IS NULL) OR
                          (completed_at IS NOT NULL AND completed_at >= 0 AND completed_day IS NOT NULL AND completed_day >= 719162 AND completed_day <= 3651693)));
                CREATE INDEX todos_pending ON todos(planned_day) WHERE completed_at IS NULL;
                CREATE INDEX todos_completed ON todos(completed_day) WHERE completed_at IS NOT NULL;
                PRAGMA user_version=4;
                """, transaction);
            transaction.Commit();
        }
    }

    public TrackerSettings ReadSettings()
    {
        var json = Scalar("SELECT json FROM settings WHERE id=1;") as string;
        return json is null ? new TrackerSettings() : (JsonSerializer.Deserialize<TrackerSettings>(json)
            ?? throw new InvalidDataException("Invalid settings.")).Validate();
    }

    public void WriteSettings(TrackerSettings settings, long? reclassifyUntil = null)
    {
        settings = settings.Validate();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO settings(id,json) VALUES(1,$json) ON CONFLICT(id) DO UPDATE SET json=$json;";
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(settings.Validate()));
        command.ExecuteNonQuery();
        if (reclassifyUntil.HasValue)
        {
            command.CommandText = "INSERT INTO rule_revisions(until,processes) VALUES($until,$processes);";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$until", reclassifyUntil.Value);
            command.Parameters.AddWithValue("$processes", JsonSerializer.Serialize(settings.WorkProcesses));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void Apply(Transition transition)
    {
        using var transaction = connection.BeginTransaction();
        if (transition.Pause is { } pause)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE pauses SET end=$end WHERE end=$start;";
            command.Parameters.AddWithValue("$start", pause.Start);
            command.Parameters.AddWithValue("$end", pause.End);
            if (command.ExecuteNonQuery() == 0)
            {
                command.CommandText = "INSERT INTO pauses(start,end) VALUES($start,$end);";
                command.ExecuteNonQuery();
            }
        }
        if (transition.Slice is { } slice)
        {
            using var last = connection.CreateCommand();
            last.Transaction = transaction;
            last.CommandText = "SELECT id,end,executable,category,browser FROM activity ORDER BY start DESC,id DESC LIMIT 1;";
            var browserJson = slice.Browser is null ? null : JsonSerializer.Serialize(slice.Browser);
            long? mergeId = null;
            using (var reader = last.ExecuteReader())
            {
                if (reader.Read())
                {
                    if (reader.GetInt64(1) > slice.Start) throw new InvalidOperationException("Overlapping activity intervals.");
                    if (reader.GetInt64(1) == slice.Start && reader.GetString(2) == slice.Executable && reader.GetInt32(3) == (int)slice.Category &&
                        (reader.IsDBNull(4) ? null : reader.GetString(4)) == browserJson)
                        mergeId = reader.GetInt64(0);
                }
            }
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = mergeId.HasValue ? "UPDATE activity SET end=$end WHERE id=$id;" :
                "INSERT INTO activity(start,end,executable,category,browser) VALUES($start,$end,$exe,$category,$browser);";
            insert.Parameters.AddWithValue("$id", mergeId ?? 0);
            insert.Parameters.AddWithValue("$start", slice.Start);
            insert.Parameters.AddWithValue("$end", slice.End);
            insert.Parameters.AddWithValue("$exe", slice.Executable);
            insert.Parameters.AddWithValue("$category", (int)slice.Category);
            insert.Parameters.AddWithValue("$browser", (object?)browserJson ?? DBNull.Value);
            insert.ExecuteNonQuery();
        }
        if (transition.Away is { } away && away.End > away.Start)
        {
            // Preserve raw activity. Consolidate only overlapping/adjacent overlays of the same kind.
            using var bounds = connection.CreateCommand();
            bounds.Transaction = transaction;
            bounds.CommandText = "SELECT MIN(start),MAX(end) FROM away WHERE end >= $start AND start <= $end AND reason=$reason;";
            AddAwayParameters(bounds, away);
            using (var reader = bounds.ExecuteReader())
            {
                if (reader.Read() && !reader.IsDBNull(0))
                    away = away with { Start = Math.Min(away.Start, reader.GetInt64(0)), End = Math.Max(away.End, reader.GetInt64(1)) };
            }
            using var write = connection.CreateCommand();
            write.Transaction = transaction;
            write.CommandText = """
                DELETE FROM away WHERE end >= $start AND start <= $end AND reason=$reason;
                INSERT INTO away(start,end,reason) VALUES($start,$end,$reason);
                """;
            AddAwayParameters(write, away);
            write.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public DayReport ReadDay(DateOnly date, TimeZoneInfo zone)
    {
        var (start, end) = Reports.Bounds(date, zone);
        var raw = new List<Slice>();
        var away = new List<AwayRange>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT start,end,executable,category,browser FROM activity WHERE start < $end AND end > $start ORDER BY start;";
        command.Parameters.AddWithValue("$start", start);
        command.Parameters.AddWithValue("$end", end);
        using (var reader = command.ExecuteReader())
            while (reader.Read()) raw.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), (Category)reader.GetInt32(3),
                reader.IsDBNull(4) ? null : JsonSerializer.Deserialize<BrowserPage>(reader.GetString(4))));
        command.CommandText = "SELECT start,end,reason FROM away WHERE start < $end AND end > $start ORDER BY start;";
        using (var reader = command.ExecuteReader())
            while (reader.Read()) away.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2)));
        var corrections = new List<Correction>();
        command.CommandText = "SELECT id,action_id,start,end,category FROM corrections WHERE undone=0 AND start < $end AND end > $start ORDER BY id;";
        using (var reader = command.ExecuteReader())
            while (reader.Read()) corrections.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3), (Category)reader.GetInt32(4)));
        var revisions = new List<RuleRevision>();
        command.CommandText = "SELECT id,until,processes FROM rule_revisions WHERE until > $start ORDER BY id;";
        using (var reader = command.ExecuteReader())
            while (reader.Read()) revisions.Add(new(reader.GetInt64(0), reader.GetInt64(1), JsonSerializer.Deserialize<string[]>(reader.GetString(2))!));
        var pauses = new List<TimeRange>();
        command.CommandText = "SELECT start,end FROM pauses WHERE start < $end AND end > $start ORDER BY start;";
        using (var reader = command.ExecuteReader())
            while (reader.Read()) pauses.Add(new(Math.Max(start, reader.GetInt64(0)), Math.Min(end, reader.GetInt64(1))));
        return Reports.Day(date, zone, raw, away, corrections, revisions) with { Pauses = pauses, CompletedTodos = ReadCompletedTodos(date) };
    }

    public string Correct(IReadOnlyList<TimeRange> ranges, Category category, long now)
    {
        if (ranges is null || ranges.Count is < 1 or > 500 || !Enum.IsDefined(category))
            throw new ArgumentException("Выберите от 1 до 500 интервалов и допустимую категорию.");
        var actionId = Guid.NewGuid().ToString("N");
        using var transaction = connection.BeginTransaction();
        foreach (var range in ranges)
        {
            if (range is null || range.Start < 0 || range.End <= range.Start || range.End > now || range.End - range.Start > 31L * 86400000)
                throw new ArgumentException("Неверные границы интервала: конец должен быть позже начала и не в будущем.");
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM activity WHERE start < $end AND end > $start;";
            command.Parameters.AddWithValue("$start", range.Start);
            command.Parameters.AddWithValue("$end", range.End);
            if (Convert.ToInt64(command.ExecuteScalar()) == 0) throw new ArgumentException("В выбранном интервале нет данных.");
            command.CommandText = "INSERT INTO corrections(action_id,start,end,category,created) VALUES($action,$start,$end,$category,$created);";
            command.Parameters.AddWithValue("$action", actionId);
            command.Parameters.AddWithValue("$category", (int)category);
            command.Parameters.AddWithValue("$created", now);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        return actionId;
    }

    public bool Undo(string actionId)
    {
        if (!Guid.TryParseExact(actionId, "N", out _)) throw new ArgumentException("Неверный идентификатор исправления.");
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE corrections SET undone=1 WHERE action_id=$id AND undone=0;";
        command.Parameters.AddWithValue("$id", actionId);
        return command.ExecuteNonQuery() > 0;
    }

    private static void AddAwayParameters(SqliteCommand command, AwayRange range)
    {
        command.Parameters.AddWithValue("$start", range.Start);
        command.Parameters.AddWithValue("$end", range.End);
        command.Parameters.AddWithValue("$reason", range.Reason);
    }
    private object? Scalar(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
    private void Execute(string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
    public void Dispose() => connection.Dispose();
}
