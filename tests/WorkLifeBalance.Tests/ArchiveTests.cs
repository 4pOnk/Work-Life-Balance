using Microsoft.Data.Sqlite;
using WorkLifeBalance.Application;
using WorkLifeBalance.Domain;
using WorkLifeBalance.Infrastructure;
using Xunit;

namespace WorkLifeBalance.Tests;

public sealed class ArchiveTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "wlb-archive-" + Guid.NewGuid());
    private string Database => Path.Combine(directory, "activity.db");
    private static readonly DateOnly Day = new(2026, 9, 19);
    private static readonly long Start = Reports.Bounds(Day, TimeZoneInfo.Utc).Start;
    private static readonly TrackerState State = new(Start, Start, new("blender.exe"), Category.Work);

    [Fact]
    public void Delete_splits_all_layers_and_restore_preserves_settings_and_undo()
    {
        using var store = new SqliteTrackerStore(Database);
        store.Apply(new(State, new(Start, Start + 10000, "blender.exe", Category.Work), new(Start, Start + 10000, "AutomaticAfk"), null));
        var action = store.Correct([new(Start, Start + 10000)], Category.Rest, Start + 20000);
        store.Apply(new(State, new(Start + 10000, Start + 20000, "Code.exe", Category.Work), null, null));
        var copy = store.Delete(new(Start + 3000, Start + 7000));
        Assert.Equal(6, store.ReadDay(Day, TimeZoneInfo.Utc).RestSeconds);
        Assert.Equal(10, store.ReadDay(Day, TimeZoneInfo.Utc).WorkSeconds);
        // The split inserted a new ID into the past; subsequent collection must still append by time.
        store.Apply(new(State, new(Start + 20000, Start + 21000, "Code.exe", Category.Work), null, null));
        Assert.Equal(11, store.ReadDay(Day, TimeZoneInfo.Utc).WorkSeconds);
        Assert.True(store.Undo(action));
        Assert.Equal(6, store.ReadDay(Day, TimeZoneInfo.Utc).AfkSeconds);
        store.WriteSettings(new() { Port = 47839, RetentionDays = 60 });
        store.Restore(copy.Id, Start + 30000);
        Assert.Equal(10, store.ReadDay(Day, TimeZoneInfo.Utc).RestSeconds);
        Assert.Equal(47839, store.ReadSettings().Port);
        Assert.Equal(60, store.ReadSettings().RetentionDays);
        Assert.True(store.Undo(action));
        Assert.Equal(10, store.ReadDay(Day, TimeZoneInfo.Utc).AfkSeconds);
        Assert.Equal(2, store.Inspect().Backups.Count);
    }

    [Theory]
    [InlineData(0, 3, 7)]
    [InlineData(3, 7, 6)]
    [InlineData(7, 10, 7)]
    [InlineData(0, 10, 0)]
    public void Pause_deletion_keeps_only_outside_range(int from, int to, int remaining)
    {
        using var store = new SqliteTrackerStore(Database);
        store.Apply(new(State, null, null, null, new(Start, Start + 10000)));
        store.Delete(new(Start + from * 1000, Start + to * 1000));
        Assert.Equal(remaining * 1000, store.ReadDay(Day, TimeZoneInfo.Utc).Pauses!.Sum(x => x.End - x.Start));
    }

    [Fact]
    public void Invalid_backup_does_not_change_live_history()
    {
        using var store = new SqliteTrackerStore(Database);
        store.Apply(new(State, new(Start, Start + 1000, "Code.exe", Category.Work), null, null));
        var copy = store.Backup();
        File.WriteAllText(Path.Combine(directory, "backups", copy.Id + ".db"), "not a database");
        Assert.Throws<SqliteException>(() => store.Restore(copy.Id, Start + 2000));
        Assert.Equal(1, store.ReadDay(Day, TimeZoneInfo.Utc).WorkSeconds);
        Assert.Throws<ArgumentException>(() => store.Restore("../activity", Start));
    }

    [Fact]
    public void Splitting_corrections_preserves_priority_over_newer_unsplit_corrections()
    {
        using var store = new SqliteTrackerStore(Database);
        store.Apply(new(State, new(Start, Start + 10000, "Code.exe", Category.Work), null, null));
        store.Correct([new(Start, Start + 10000)], Category.Rest, Start + 20000);
        var last = store.Correct([new(Start + 8000, Start + 10000)], Category.Afk, Start + 20000);
        store.Delete(new(Start + 3000, Start + 7000));
        var report = store.ReadDay(Day, TimeZoneInfo.Utc);
        Assert.Equal(4, report.RestSeconds);
        Assert.Equal(2, report.AfkSeconds);
        Assert.True(store.Undo(last));
        Assert.Equal(6, store.ReadDay(Day, TimeZoneInfo.Utc).RestSeconds);
    }

    [Fact]
    public void Future_backup_and_invalid_delete_are_rejected()
    {
        using var store = new SqliteTrackerStore(Database);
        store.Apply(new(State, new(Start, Start + 1000, "Code.exe", Category.Work), null, null));
        var copy = store.Backup();
        Assert.Throws<ArgumentException>(() => store.Restore(copy.Id, Start));
        Assert.Throws<ArgumentException>(() => store.Delete(new(Start, Start)));
        Assert.Equal(1, store.ReadDay(Day, TimeZoneInfo.Utc).WorkSeconds);
    }

    [Fact]
    public void Csv_quotes_and_neutralizes_formulas_without_modifying_json_source()
    {
        var title = "  =HYPERLINK(\"bad\")\r\nnext";
        var day = Reports.Day(Day, TimeZoneInfo.Utc, [new(Start, Start + 1000, "tool.exe", Category.Rest,
            new("https://example.test/path", "example.test", title, false, 1, 1))], []);
        var csv = ExportFormat.Csv(new(1, "UTC", Start, [day]));
        Assert.Contains("\"'  =HYPERLINK(\"\"bad\"\")\r\nnext\"", csv);
        Assert.Equal(title, day.Intervals.Single().Browser!.Title);
    }

    [Fact]
    public void Unwritable_backup_location_prevents_deletion()
    {
        using var store = new SqliteTrackerStore(Database);
        store.Apply(new(State, new(Start, Start + 1000, "Code.exe", Category.Work), null, null));
        File.WriteAllText(Path.Combine(directory, "backups"), "blocking file");
        Assert.Throws<IOException>(() => store.Delete(new(Start, Start + 1000)));
        Assert.Equal(1, store.ReadDay(Day, TimeZoneInfo.Utc).WorkSeconds);
    }

    [Fact]
    public void Logs_do_not_include_exception_secrets()
    {
        new LocalLog(directory).Error("test", new IOException("https://secret.test/?token=SECRET"));
        var log = File.ReadAllText(Path.Combine(directory, "errors.log"));
        Assert.Contains("IOException", log);
        Assert.DoesNotContain("SECRET", log);
        Assert.DoesNotContain("secret.test", log);
    }

    [Fact]
    public void Corrupt_database_is_not_recreated_and_file_handle_is_released()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Database, "corrupt synthetic database");
        var original = File.ReadAllBytes(Database);
        Assert.Throws<SqliteException>(() => new SqliteTrackerStore(Database));
        SqliteConnection.ClearAllPools();
        Assert.Equal(original, File.ReadAllBytes(Database));
        using var exclusive = new FileStream(Database, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(original.Length, exclusive.Length);
    }

    [Fact]
    public void Newer_schema_is_rejected_without_mutating_file()
    {
        Directory.CreateDirectory(directory);
        using (var connection = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=99;";
            command.ExecuteNonQuery();
        }
        var original = File.ReadAllBytes(Database);
        Assert.Throws<InvalidOperationException>(() => new SqliteTrackerStore(Database));
        SqliteConnection.ClearAllPools();
        Assert.Equal(original, File.ReadAllBytes(Database));
    }

    [Fact]
    public void Unavailable_database_is_not_deleted()
    {
        using (var store = new SqliteTrackerStore(Database)) store.WriteSettings(new() { AfkMinutes = 120 });
        SqliteConnection.ClearAllPools();
        using (var file = new FileStream(Database, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<SqliteException>(() => new SqliteTrackerStore(Database));
        using var reopened = new SqliteTrackerStore(Database);
        Assert.Equal(120, reopened.ReadSettings().AfkMinutes);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
