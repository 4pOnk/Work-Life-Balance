using WorkLifeBalance.Domain;
using WorkLifeBalance.Infrastructure;
using Xunit;

namespace WorkLifeBalance.Tests;

public sealed class DashboardTests
{
    [Theory]
    [InlineData(2026, 3, 29, 23)]
    [InlineData(2026, 10, 25, 25)]
    public void DstHoursPreserveDuration(int year, int month, int day, int hours)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        var date = new DateOnly(year, month, day);
        var (start, end) = Reports.Bounds(date, zone);
        var report = Reports.Day(date, zone, [new(start, end, "Code.exe", Category.Work)], []);
        var detail = Dashboard.Detail(report, zone, end);
        Assert.Equal(hours, detail.Hours.Count);
        Assert.Equal(hours * 3600, detail.Hours.Sum(x => x.WorkSeconds));
        Assert.Equal(report.WorkSeconds, detail.Apps.Sum(x => x.WorkSeconds));
        Assert.Equal(hours, detail.Hours.Select(x => x.Label).Distinct().Count());
        Assert.Equal(0, detail.UnknownSeconds);
    }

    [Fact]
    public void PartialCorrectionAfkAndUndoAreReflectedInEveryProjection()
    {
        var date = new DateOnly(2026, 9, 1);
        var (start, end) = Reports.Bounds(date, TimeZoneInfo.Utc);
        Slice[] raw = [new(start, start + 7200000, "firefox.exe", Category.Rest)];
        AwayRange[] afk = [new(start + 3600000, start + 7200000, "AutomaticAfk")];
        Correction[] edits = [new(1, "edit", start + 5400000, start + 7200000, Category.Work)];
        var report = Reports.Day(date, TimeZoneInfo.Utc, raw, afk, edits);
        var detail = Dashboard.Detail(report, TimeZoneInfo.Utc, end);
        var month = Dashboard.Month(date, TimeZoneInfo.Utc, [report], end);
        Assert.Equal(1800, detail.Hours.Sum(x => x.WorkSeconds));
        Assert.Equal(1800, month.Totals.WorkSeconds);
        Assert.Equal(1800, month.Weeks.Single().Totals.WorkSeconds);
        Assert.Equal(1800, detail.Apps.Single().AfkSeconds);
        var undone = Reports.Day(date, TimeZoneInfo.Utc, raw, afk);
        Assert.Equal(0, Dashboard.Detail(undone, TimeZoneInfo.Utc, end).Hours.Sum(x => x.WorkSeconds));
    }

    [Fact]
    public void MonthDistinguishesEmptyZeroAndFutureAndClipsWeeksToMonth()
    {
        var first = new DateOnly(2026, 2, 1);
        var now = Reports.Bounds(first.AddDays(2), TimeZoneInfo.Utc).Start;
        var days = Enumerable.Range(0, 28).Select(i =>
        {
            var date = first.AddDays(i);
            var (start, _) = Reports.Bounds(date, TimeZoneInfo.Utc);
            return Reports.Day(date, TimeZoneInfo.Utc, i == 0 ? [new(start, start + 60000, "firefox.exe", Category.Rest)] : [], []);
        }).ToArray();
        var report = Dashboard.Month(first, TimeZoneInfo.Utc, days, now);
        Assert.Equal(28, report.Days.Count);
        Assert.Equal("recorded", report.Days[0].State);
        Assert.Equal(0, report.Days[0].Level);
        Assert.Equal("empty", report.Days[1].State);
        Assert.Equal("future", report.Days[3].State);
        Assert.Equal("2026-02-01", report.Weeks[0].Start);
        Assert.Equal("2026-02-28", report.Weeks[^1].End);
        Assert.Equal(report.Totals.RestSeconds, report.Weeks.Sum(x => x.Totals.RestSeconds));
    }

    [Fact]
    public void PauseUnknownFutureAndProvisionalDoNotInflateActiveTime()
    {
        var date = new DateOnly(2026, 9, 1);
        var (start, _) = Reports.Bounds(date, TimeZoneInfo.Utc);
        var report = Reports.Day(date, TimeZoneInfo.Utc, [new(start, start + 60000, "Code.exe", Category.Work)], [])
            with
        { Pauses = [new(start + 60000, start + 120000)] };
        var detail = Dashboard.Detail(report, TimeZoneInfo.Utc, start + 180000, start + 30000);
        Assert.Equal(60, detail.PauseSeconds);
        Assert.Equal(60, detail.UnknownSeconds);
        Assert.Equal(30, detail.ProvisionalSeconds);
        Assert.Equal(86400 - 180, detail.FutureSeconds);
        Assert.Equal(60, detail.Report.WorkSeconds);
        Assert.Null(Dashboard.Sum([]).WorkShare);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(3600, 2)]
    [InlineData(14400, 3)]
    [InlineData(28800, 4)]
    public void CalendarScaleHasFixedBoundaries(double seconds, int level) => Assert.Equal(level, Dashboard.Level(seconds));

    [Fact]
    public void PausesPersistAndDoNotFillTrackerDowntime()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wlb-dashboard-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var date = new DateOnly(2026, 9, 1);
            var start = Reports.Bounds(date, TimeZoneInfo.Utc).Start;
            var settings = new TrackerSettings();
            var state = new TrackerState(start, start, new("Code.exe"), Category.Work) { Paused = true };
            using (var store = new SqliteTrackerStore(Path.Combine(directory, "activity.db")))
            {
                var step = Tracker.Advance(state, new(start + 1000, start, new("Code.exe")), settings);
                store.Apply(step);
                var next = Tracker.Advance(step.State, new(start + 2000, start, new("Code.exe")), settings);
                store.Apply(next);
                store.Apply(Tracker.Advance(next.State, new(start + 10000, start, new("Code.exe")), settings));
            }
            using var reopened = new SqliteTrackerStore(Path.Combine(directory, "activity.db"));
            var report = reopened.ReadDay(date, TimeZoneInfo.Utc);
            Assert.Equal(new TimeRange(start, start + 2000), Assert.Single(report.Pauses!));
            Assert.Empty(report.Intervals);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }
}
