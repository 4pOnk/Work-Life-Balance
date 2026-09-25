using Microsoft.Data.Sqlite;
using WorkLifeBalance.Application;
using WorkLifeBalance.Domain;

namespace WorkLifeBalance.Infrastructure;

public sealed partial class SqliteTrackerStore
{
    private string BackupDirectory => Path.Combine(Path.GetDirectoryName(databasePath)!, "backups");
    private static readonly string[] HistoryTables = ["activity", "away", "corrections", "rule_revisions", "pauses"];

    public StorageInfo Inspect()
    {
        var first = Scalar("SELECT MIN(start) FROM (SELECT start FROM activity UNION ALL SELECT start FROM pauses UNION ALL SELECT completed_at AS start FROM todos WHERE completed_at IS NOT NULL);");
        var bytes = new[] { databasePath, databasePath + "-wal", databasePath + "-shm" }
            .Where(File.Exists).Sum(x => new FileInfo(x).Length);
        var backups = Directory.Exists(BackupDirectory) ? Directory.EnumerateFiles(BackupDirectory, "*.db")
            .Where(x => Guid.TryParseExact(Path.GetFileNameWithoutExtension(x), "N", out _))
            .Select(Info).OrderByDescending(x => x.Created).ToArray() : [];
        return new(bytes, first is null or DBNull ? null : Convert.ToInt64(first), backups);
    }

    public BackupInfo Backup()
    {
        Directory.CreateDirectory(BackupDirectory);
        var path = Path.Combine(BackupDirectory, Guid.NewGuid().ToString("N") + ".db");
        var temporary = path + ".tmp";
        try
        {
            using (var target = Open(temporary, SqliteOpenMode.ReadWriteCreate)) connection.BackupDatabase(target);
            using (var check = Open(temporary, SqliteOpenMode.ReadOnly)) ValidateBackup(check, long.MaxValue);
            File.Move(temporary, path);
            return Info(path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public BackupInfo Restore(string id, long now)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Неверный идентификатор копии.");
        var path = Path.Combine(BackupDirectory, id + ".db");
        if (!File.Exists(path)) throw new ArgumentException("Копия не найдена.");
        using var source = Open(path, SqliteOpenMode.ReadOnly);
        ValidateBackup(source, now);
        using var version = source.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        var hasTodos = Convert.ToInt32(version.ExecuteScalar()) >= 4;
        var safety = Backup();
        using var attach = connection.CreateCommand();
        attach.CommandText = "ATTACH DATABASE $path AS archive;";
        attach.Parameters.AddWithValue("$path", path);
        attach.ExecuteNonQuery();
        try
        {
            using var transaction = connection.BeginTransaction();
            foreach (var table in HistoryTables)
                Execute($"DELETE FROM main.{table}; INSERT INTO main.{table} SELECT * FROM archive.{table};", transaction);
            if (hasTodos) Execute("DELETE FROM main.todos; INSERT INTO main.todos SELECT * FROM archive.todos;", transaction);
            transaction.Commit();
        }
        finally { Execute("DETACH DATABASE archive;"); }
        return safety;
    }

    public BackupInfo Delete(TimeRange range)
    {
        if (range.Start < 0 || range.End <= range.Start) throw new ArgumentException("Неверные границы удаления.");
        var safety = Backup();
        using var transaction = connection.BeginTransaction();
        // Split spanning rows before trimming; retain correction action IDs so undo remains coherent.
        foreach (var (table, columns) in new[] {
            ("activity", ",executable,category,browser"), ("away", ",reason"), ("pauses", "") })
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO {table}(start,end{columns}) SELECT $end,end{columns} FROM {table} WHERE start < $start AND end > $end;
                DELETE FROM {table} WHERE start >= $start AND end <= $end;
                UPDATE {table} SET end=$start WHERE start < $start AND end > $start;
                UPDATE {table} SET start=$end WHERE start >= $start AND start < $end AND end > $end;
                """;
            command.Parameters.AddWithValue("$start", range.Start);
            command.Parameters.AddWithValue("$end", range.End);
            command.ExecuteNonQuery();
        }
        // Correction IDs encode precedence. Reinsert both halves in original priority order.
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                CREATE TEMP TABLE kept_corrections AS
                    SELECT id,action_id,start,MIN(end,$start) AS end,category,created,undone FROM corrections WHERE start < $start
                    UNION ALL
                    SELECT id,action_id,MAX(start,$end),end,category,created,undone FROM corrections WHERE end > $end;
                DELETE FROM corrections;
                INSERT INTO corrections(action_id,start,end,category,created,undone)
                    SELECT action_id,start,end,category,created,undone FROM kept_corrections ORDER BY id,start;
                DROP TABLE kept_corrections;
                """;
            command.Parameters.AddWithValue("$start", range.Start);
            command.Parameters.AddWithValue("$end", range.End);
            command.ExecuteNonQuery();
        }
        Execute("DELETE FROM rule_revisions WHERE NOT EXISTS (SELECT 1 FROM activity WHERE activity.start < rule_revisions.until);", transaction);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM todos WHERE completed_at >= $start AND completed_at < $end;";
            command.Parameters.AddWithValue("$start", range.Start);
            command.Parameters.AddWithValue("$end", range.End);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        return safety;
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var value = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false, DefaultTimeout = 3 }.ToString());
        try { value.Open(); return value; }
        catch { value.Dispose(); throw; }
    }

    private static void ValidateBackup(SqliteConnection source, long now)
    {
        using var command = source.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        if (command.ExecuteScalar() as string != "ok") throw new ArgumentException("Копия повреждена.");
        command.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(command.ExecuteScalar());
        if (version is not (3 or 4)) throw new ArgumentException("Неподдерживаемая версия копии. Нужна база версии 3 или 4.");
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='trigger';";
        if (Convert.ToInt64(command.ExecuteScalar()) != 0) throw new ArgumentException("Копия содержит неподдерживаемые триггеры.");
        foreach (var table in HistoryTables.Where(x => x != "rule_revisions"))
        {
            command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE start < 0 OR end <= start OR end > $now;";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$now", now);
            if (Convert.ToInt64(command.ExecuteScalar()) != 0) throw new ArgumentException("В копии неверные границы времени или данные из будущего.");
        }
        command.Parameters.Clear();
        foreach (var table in new[] { "activity", "pauses" })
        {
            command.CommandText = $"SELECT COUNT(*) FROM (SELECT start,LAG(end) OVER (ORDER BY start) AS previous_end FROM {table}) WHERE start < previous_end;";
            if (Convert.ToInt64(command.ExecuteScalar()) != 0) throw new ArgumentException("В копии пересекаются исходные интервалы.");
        }
        command.CommandText = "SELECT COUNT(*) FROM activity WHERE category NOT IN (0,1,2) OR (browser IS NOT NULL AND NOT json_valid(browser));";
        if (Convert.ToInt64(command.ExecuteScalar()) != 0) throw new ArgumentException("В копии неверные данные активности.");
        command.CommandText = "SELECT COUNT(*) FROM corrections WHERE category NOT IN (0,1,2);";
        if (Convert.ToInt64(command.ExecuteScalar()) != 0) throw new ArgumentException("В копии неверные исправления.");
        if (version >= 4)
        {
            command.CommandText = """
                SELECT COUNT(*) FROM todos WHERE id IS NULL OR length(id) != 32 OR id GLOB '*[^0-9a-f]*'
                OR title IS NULL OR length(trim(title)) NOT BETWEEN 1 AND 240
                OR weight IS NULL OR weight NOT BETWEEN 1 AND 5 OR planned_day IS NULL OR planned_day NOT BETWEEN 719162 AND 3651693
                OR (completed_at IS NULL) != (completed_day IS NULL)
                OR completed_at < 0 OR completed_at > $now OR completed_day NOT BETWEEN 719162 AND 3651693;
                """;
            command.Parameters.AddWithValue("$now", now);
            if (Convert.ToInt64(command.ExecuteScalar()) != 0) throw new ArgumentException("В копии неверные задачи.");
        }
    }

    private static BackupInfo Info(string path)
    {
        var file = new FileInfo(path);
        return new(Path.GetFileNameWithoutExtension(path), new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeMilliseconds(), file.Length);
    }
}
