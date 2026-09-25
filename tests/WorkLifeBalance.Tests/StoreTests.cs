using WorkLifeBalance.Domain;
using WorkLifeBalance.Infrastructure;
using Xunit;

namespace WorkLifeBalance.Tests;

public sealed class StoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "wlb-tests-" + Guid.NewGuid());
    private string Database => Path.Combine(directory, "test.db");
    private static readonly DateOnly Day = new(2026, 9, 19);
    private static readonly long Midnight = Reports.Bounds(Day, TimeZoneInfo.Utc).Start;
    private static readonly TrackerState State = new(Midnight, Midnight, new("blender.exe"), Category.Work);

    [Fact]
    public void Retroactive_overlay_is_atomic_and_preserves_raw_time_across_midnight()
    {
        using var store = new SqliteTrackerStore(Database);
        store.Apply(new(State, new(Midnight - 60_000, Midnight + 60_000, "blender.exe", Category.Work), null, null));
        Assert.Equal(60, store.ReadDay(Day, TimeZoneInfo.Utc).WorkSeconds);
        store.Apply(new(State, null, new(Midnight - 30_000, Midnight + 60_000, "AutomaticAfk"), null));
        var yesterday = store.ReadDay(Day.AddDays(-1), TimeZoneInfo.Utc);
        Assert.Equal(30, yesterday.WorkSeconds);
        Assert.Equal(30, yesterday.AfkSeconds);
        Assert.Equal(60, store.ReadDay(Day, TimeZoneInfo.Utc).AfkSeconds);
        store.Apply(new(State, null, new(Midnight - 30_000, Midnight + 60_000, "AutomaticAfk"), null));
        Assert.Equal(60, store.ReadDay(Day, TimeZoneInfo.Utc).AfkSeconds);
    }

    [Fact]
    public void Recovery_uses_last_checkpoint_and_leaves_downtime_unknown()
    {
        using (var store = new SqliteTrackerStore(Database))
        {
            store.WriteSettings(new TrackerSettings { AfkMinutes = 120, WorkProcesses = ["tool.exe"] });
            store.Apply(new(State, new(Midnight, Midnight + 1000, "blender.exe", Category.Work), null, null));
            store.Apply(new(State, new(Midnight + 1000, Midnight + 2000, "blender.exe", Category.Work), null, null));
        }
        using var recovered = new SqliteTrackerStore(Database);
        Assert.Equal(120, recovered.ReadSettings().AfkMinutes);
        Assert.Single(recovered.ReadDay(Day, TimeZoneInfo.Utc).Intervals);
        recovered.Apply(new(State, new(Midnight + 60_000, Midnight + 61_000, "blender.exe", Category.Work), null, null));
        var report = recovered.ReadDay(Day, TimeZoneInfo.Utc);
        Assert.Equal(3, report.WorkSeconds);
        Assert.Equal(2, report.Intervals.Count);
    }

    [Fact]
    public void Invalid_overlap_rolls_back_entire_transaction()
    {
        using var store = new SqliteTrackerStore(Database);
        store.Apply(new(State, new(Midnight, Midnight + 1000, "blender.exe", Category.Work), null, null));
        Assert.Throws<InvalidOperationException>(() => store.Apply(new(State,
            new(Midnight + 500, Midnight + 2000, "other.exe", Category.Rest),
            new(Midnight, Midnight + 2000, "AutomaticAfk"), null)));
        var report = store.ReadDay(Day, TimeZoneInfo.Utc);
        Assert.Equal(1, report.WorkSeconds);
        Assert.Equal(0, report.AfkSeconds);
    }

    [Fact]
    public void Overlapping_absence_reasons_never_double_count()
    {
        var report = Reports.Day(Day, TimeZoneInfo.Utc,
            [new(Midnight, Midnight + 10_000, "test.exe", Category.Work)],
            [new(Midnight, Midnight + 6000, "AutomaticAfk"), new(Midnight + 2000, Midnight + 8000, "ManualAfk")]);
        Assert.Equal(8, report.AfkSeconds);
        Assert.Equal(2, report.WorkSeconds);
        Assert.Equal(10_000, report.Intervals.Sum(x => x.End - x.Start));
    }

    [Theory]
    [InlineData(2026, 3, 29, 23)]
    [InlineData(2026, 10, 25, 25)]
    public void Day_bounds_follow_dst(int year, int month, int day, int hours)
    {
        var bounds = Reports.Bounds(new(year, month, day), TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"));
        Assert.Equal(hours, (bounds.End - bounds.Start) / 3_600_000d);
    }

    [Fact]
    public void Empty_day_has_no_work_percentage() =>
        Assert.Null(Reports.Day(Day, TimeZoneInfo.Utc, [], []).WorkShare);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
