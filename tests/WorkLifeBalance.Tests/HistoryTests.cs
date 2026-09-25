using Microsoft.Data.Sqlite;
using WorkLifeBalance.Domain;
using WorkLifeBalance.Infrastructure;
using Xunit;

namespace WorkLifeBalance.Tests;

public sealed class HistoryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "wlb-history-" + Guid.NewGuid());
    private string Database => Path.Combine(directory, "test.db");
    private static readonly DateOnly Day = new(2026, 9, 19);
    private static readonly long Start = Reports.Bounds(Day, TimeZoneInfo.Utc).Start;
    private static readonly TrackerState State = new(Start, Start, new("tool.exe"), Category.Rest);

    [Fact]
    public void Partial_correction_splits_without_losing_time_and_undo_restores_afk()
    {
        using var store = new SqliteTrackerStore(Database);
        store.Apply(new(State, new(Start, Start + 10000, "tool.exe", Category.Rest), new(Start, Start + 10000, "AutomaticAfk"), null));
        var id = store.Correct([new(Start + 2000, Start + 7000)], Category.Work, Start + 20000);
        var day = store.ReadDay(Day, TimeZoneInfo.Utc);
        Assert.Equal(5, day.WorkSeconds); Assert.Equal(5, day.AfkSeconds);
        Assert.Equal(3, day.Intervals.Count);
        Assert.Equal("manualEdit", day.Intervals[1].Source);
        Assert.Equal(id, day.Intervals[1].CorrectionId);
        Assert.True(store.Undo(id)); Assert.False(store.Undo(id));
        Assert.Equal(10, store.ReadDay(Day, TimeZoneInfo.Utc).AfkSeconds);
    }

    [Fact]
    public void Batch_correction_survives_restart_and_rule_recalculation()
    {
        string id;
        using (var store = new SqliteTrackerStore(Database))
        {
            store.Apply(new(State, new(Start, Start + 10000, "tool.exe", Category.Rest), null, null));
            id = store.Correct([new(Start, Start + 2000), new(Start + 5000, Start + 8000)], Category.Work, Start + 10000);
            store.WriteSettings(new TrackerSettings { WorkProcesses = [] }, Start + 10000);
        }
        using var restored = new SqliteTrackerStore(Database);
        Assert.Equal(5, restored.ReadDay(Day, TimeZoneInfo.Utc).WorkSeconds);
        restored.Undo(id);
        Assert.Equal(0, restored.ReadDay(Day, TimeZoneInfo.Utc).WorkSeconds);
    }

    [Fact]
    public void Latest_overlapping_edit_wins_and_undo_reveals_previous_edit()
    {
        using var store = new SqliteTrackerStore(Database);
        store.Apply(new(State, new(Start, Start + 10000, "tool.exe", Category.Rest), null, null));
        store.Correct([new(Start, Start + 10000)], Category.Work, Start + 10000);
        var latest = store.Correct([new(Start + 3000, Start + 5000)], Category.Rest, Start + 10000);
        Assert.Equal(8, store.ReadDay(Day, TimeZoneInfo.Utc).WorkSeconds);
        store.Undo(latest);
        Assert.Equal(10, store.ReadDay(Day, TimeZoneInfo.Utc).WorkSeconds);
    }

    [Fact]
    public void Rule_revision_stops_at_its_cutoff_even_if_raw_interval_is_merged()
    {
        using var store = new SqliteTrackerStore(Database);
        store.Apply(new(State, new(Start, Start + 10000, "tool.exe", Category.Rest), null, null));
        store.WriteSettings(new TrackerSettings { WorkProcesses = ["tool.exe"] }, Start + 5000);
        var day = store.ReadDay(Day, TimeZoneInfo.Utc);
        Assert.Equal(5, day.WorkSeconds); Assert.Equal(5, day.RestSeconds);
        store.WriteSettings(new TrackerSettings { WorkProcesses = [] });
        Assert.Equal(5, store.ReadDay(Day, TimeZoneInfo.Utc).WorkSeconds);
    }

    [Fact]
    public void Firefox_is_rest_after_rule_revision_but_can_be_manually_work()
    {
        using var store = new SqliteTrackerStore(Database);
        var page = new BrowserPage("https://example.com/docs", "example.com", "Docs", false, 1, 2);
        store.Apply(new(State, new(Start, Start + 10000, "firefox.exe", Category.Rest, page), null, null));
        store.WriteSettings(new TrackerSettings { WorkProcesses = ["firefox.exe"] }, Start + 10000);
        var day = store.ReadDay(Day, TimeZoneInfo.Utc);
        Assert.Equal(10, day.RestSeconds); Assert.Equal(page, day.Intervals[0].Browser);
        Assert.Equal("browserDefault", day.Intervals[0].Source);
        store.Correct([new(Start, Start + 10000)], Category.Work, Start + 10000);
        Assert.Equal(10, store.ReadDay(Day, TimeZoneInfo.Utc).WorkSeconds);
    }

    [Fact]
    public void Invalid_batch_rolls_back_and_corrections_do_not_create_data_in_gaps()
    {
        using var store = new SqliteTrackerStore(Database);
        store.Apply(new(State, new(Start, Start + 1000, "tool.exe", Category.Rest), null, null));
        store.Apply(new(State, new(Start + 5000, Start + 6000, "tool.exe", Category.Rest), null, null));
        Assert.Throws<ArgumentException>(() => store.Correct([new(Start, Start + 1000), new(Start + 9000, Start + 8000)], Category.Work, Start + 10000));
        Assert.Equal(0, store.ReadDay(Day, TimeZoneInfo.Utc).WorkSeconds);
        store.Correct([new(Start, Start + 6000)], Category.Work, Start + 10000);
        Assert.Equal(2, store.ReadDay(Day, TimeZoneInfo.Utc).WorkSeconds);
    }

    [Fact]
    public void Version_one_migration_creates_backup_and_preserves_existing_rows()
    {
        Directory.CreateDirectory(directory);
        using (var old = new SqliteConnection($"Data Source={Database}"))
        {
            old.Open(); using var sql = old.CreateCommand();
            sql.CommandText = """
                CREATE TABLE settings(id INTEGER PRIMARY KEY,json TEXT NOT NULL);
                CREATE TABLE activity(id INTEGER PRIMARY KEY,start INTEGER,end INTEGER,executable TEXT,category INTEGER);
                CREATE TABLE away(id INTEGER PRIMARY KEY,start INTEGER,end INTEGER,reason TEXT);
                INSERT INTO activity(start,end,executable,category) VALUES($start,$end,'tool.exe',0);
                PRAGMA user_version=1;
                """;
            sql.Parameters.AddWithValue("$start", Start); sql.Parameters.AddWithValue("$end", Start + 1000); sql.ExecuteNonQuery();
        }
        using var migrated = new SqliteTrackerStore(Database);
        Assert.Equal(1, migrated.ReadDay(Day, TimeZoneInfo.Utc).WorkSeconds);
        Assert.True(File.Exists(Database + ".before-v2.bak"));
        using var backup = new SqliteConnection($"Data Source={Database}.before-v2.bak"); backup.Open();
        using var version = backup.CreateCommand(); version.CommandText = "PRAGMA user_version;";
        Assert.Equal(1L, version.ExecuteScalar());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
