using Microsoft.Data.Sqlite;
using WorkLifeBalance.Application;
using WorkLifeBalance.Domain;
using WorkLifeBalance.Infrastructure;
using Xunit;

namespace WorkLifeBalance.Tests;

public sealed class TodoTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "wlb-todo-" + Guid.NewGuid());
    private string Database => Path.Combine(directory, "activity.db");
    private static readonly DateOnly Today = new(2026, 9, 25);
    private static readonly long Now = Reports.Bounds(Today, TimeZoneInfo.Utc).Start + 3600000;
    private static TodoDraft Draft(DateOnly date, string title = "Task", int weight = 1) => new(title, weight, date.ToString("yyyy-MM-dd"));

    [Fact]
    public void Complete_is_idempotent_undo_removes_recap_and_data_survives_restart()
    {
        string id;
        using (var store = new SqliteTrackerStore(Database))
        {
            var task = store.CreateTodo(Draft(Today, "  Model  ", 5), Today);
            id = task.Id;
            Assert.Equal("Model", task.Title);
            store.CompleteTodo(id, true, Today, Now);
            store.CompleteTodo(id, true, Today, Now + 1000);
            Assert.Equal(Now, store.ReadTodos(Today, Today).Items.Single().CompletedAt);
            Assert.Equal(5, store.ReadDay(Today, TimeZoneInfo.Utc).CompletedTodos!.Single().Weight);
            Assert.Equal(0, store.ReadDay(Today, TimeZoneInfo.Utc).WorkSeconds);
        }
        using var reopened = new SqliteTrackerStore(Database);
        Assert.Equal(id, reopened.ReadTodos(Today, Today).Items.Single().Id);
        reopened.CompleteTodo(id, false, Today, Now + 2000);
        Assert.Empty(reopened.ReadDay(Today, TimeZoneInfo.Utc).CompletedTodos!);
        Assert.Null(reopened.ReadTodos(Today, Today).Items.Single().CompletedAt);
        reopened.UpdateTodo(id, Draft(Today, "Updated", 2), Today);
        Assert.Equal("Updated", reopened.ReadTodos(Today, Today).Items.Single().Title);
        reopened.DeleteTodo(id, Today);
        Assert.Empty(reopened.ReadTodos(Today, Today).Items);
    }

    [Theory]
    [InlineData("2026-09-30", "2026-10-01")]
    [InlineData("2026-12-31", "2027-01-01")]
    [InlineData("2028-02-28", "2028-02-29")]
    [InlineData("2026-10-24", "2026-10-27")]
    public void Pending_rolls_forward_across_midnight_or_downtime_without_duplicates(string from, string to)
    {
        var first = DateOnly.Parse(from);
        var next = DateOnly.Parse(to);
        using var store = new SqliteTrackerStore(Database);
        var task = store.CreateTodo(Draft(first), first);
        var later = store.CreateTodo(Draft(next.AddDays(1), "Later"), first);
        Assert.Empty(store.ReadTodos(first, next).Items);
        var carried = Assert.Single(store.ReadTodos(next, next).Items);
        Assert.Equal(task.Id, carried.Id);
        Assert.Equal(to, carried.Date);
        Assert.Equal(later.Id, store.ReadTodos(next.AddDays(1), next).Items.Single().Id);
        Assert.Single(store.ReadTodos(next, next).Items);
        store.CompleteTodo(task.Id, true, next, Reports.Bounds(next, TimeZoneInfo.Utc).Start);
        Assert.Empty(store.ReadDay(first, TimeZoneInfo.Utc).CompletedTodos!);
        Assert.Single(store.ReadDay(next, TimeZoneInfo.Utc).CompletedTodos!);
    }

    [Fact]
    public void Future_tasks_cannot_complete_and_past_completed_tasks_are_immutable()
    {
        using var store = new SqliteTrackerStore(Database);
        var future = store.CreateTodo(Draft(Today.AddDays(1)), Today);
        Assert.Throws<ArgumentException>(() => store.CompleteTodo(future.Id, true, Today, Now));
        store.UpdateTodo(future.Id, Draft(Today), Today);
        store.CompleteTodo(future.Id, true, Today, Now);
        Assert.Throws<ArgumentException>(() => store.UpdateTodo(future.Id, Draft(Today.AddDays(1)), Today));
        var tomorrow = Today.AddDays(1);
        Assert.Throws<ArgumentException>(() => store.CompleteTodo(future.Id, false, tomorrow, Now + 86400000));
        Assert.Throws<ArgumentException>(() => store.UpdateTodo(future.Id, Draft(tomorrow), tomorrow));
        Assert.Throws<ArgumentException>(() => store.DeleteTodo(future.Id, tomorrow));
        Assert.Single(store.ReadTodos(Today, tomorrow).Items);
        Assert.Empty(store.ReadTodos(tomorrow, tomorrow).Items);
    }

    [Theory]
    [InlineData("", 1, "2026-09-25")]
    [InlineData("\nBad", 1, "2026-09-25")]
    [InlineData("Task", 0, "2026-09-25")]
    [InlineData("Task", 6, "2026-09-25")]
    [InlineData("Task", 1, "2026-09-24")]
    [InlineData("Task", 1, "2026-02-30")]
    [InlineData("Task", 1, "9999-01-01")]
    public void Invalid_drafts_are_rejected(string title, int weight, string date)
    {
        using var store = new SqliteTrackerStore(Database);
        Assert.Throws<ArgumentException>(() => store.CreateTodo(new(title, weight, date), Today));
        Assert.Empty(store.ReadTodos(Today, Today).Items);
    }

    [Fact]
    public void Backups_restore_tasks_and_deletion_preserves_open_plans()
    {
        using var store = new SqliteTrackerStore(Database);
        var done = store.CreateTodo(Draft(Today, "Done", 4), Today);
        store.CompleteTodo(done.Id, true, Today, Now);
        store.CreateTodo(Draft(Today, "Open"), Today);
        store.CreateTodo(Draft(Today.AddDays(1), "Future"), Today);
        var copy = store.Delete(new(Now, Now + 1));
        Assert.Single(store.ReadTodos(Today, Today).Items);
        Assert.Single(store.ReadTodos(Today.AddDays(1), Today).Items);
        store.Restore(copy.Id, Now + 1);
        Assert.Equal(2, store.ReadTodos(Today, Today).Items.Count);
        Assert.Equal(4, store.ReadDay(Today, TimeZoneInfo.Utc).CompletedTodos!.Single().Weight);
        Assert.Contains("\"Todo\"", ExportFormat.Csv(new(1, "UTC", Now, [store.ReadDay(Today, TimeZoneInfo.Utc)])));
    }

    [Fact]
    public void Legacy_copy_restore_keeps_tasks_and_v3_migration_backs_up_original()
    {
        using (var store = new SqliteTrackerStore(Database)) { }
        using (var connection = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE todos; PRAGMA user_version=3;";
            command.ExecuteNonQuery();
        }
        using var upgraded = new SqliteTrackerStore(Database);
        Assert.True(File.Exists(Database + ".before-v4.bak"));
        var task = upgraded.CreateTodo(Draft(Today), Today);
        var id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.Combine(directory, "backups"));
        File.Copy(Database + ".before-v4.bak", Path.Combine(directory, "backups", id + ".db"));
        upgraded.Restore(id, Now);
        Assert.Equal(task.Id, upgraded.ReadTodos(Today, Today).Items.Single().Id);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(1, 1)] [InlineData(4, 1)] [InlineData(5, 2)]
    [InlineData(9, 2)] [InlineData(10, 3)] [InlineData(14, 3)] [InlineData(15, 4)]
    public void Effort_scale_is_fixed(int weight, int expected) => Assert.Equal(expected, Dashboard.TaskLevel(weight));

    [Fact]
    public void Task_only_day_has_calendar_color_without_fabricated_time()
    {
        using var store = new SqliteTrackerStore(Database);
        var task = store.CreateTodo(Draft(Today, "Task", 5), Today);
        store.CompleteTodo(task.Id, true, Today, Now);
        var day = store.ReadDay(Today, TimeZoneInfo.Utc);
        var cell = Dashboard.Month(new(2026, 9, 1), TimeZoneInfo.Utc, [day], Now).Days.Single();
        Assert.Equal("recorded", cell.State);
        Assert.False(cell.HasActivity);
        Assert.Equal(0, cell.Level);
        Assert.Equal(2, cell.TaskLevel);
        Assert.Equal(5, cell.TaskWeight);
        Assert.Equal(0, cell.Totals.WorkSeconds);
    }

    [Fact]
    public async Task Coordinator_uses_server_day_serializes_commands_and_keeps_tracking_state()
    {
        using var store = new SqliteTrackerStore(Database);
        var clock = new Clock { Now = Reports.Bounds(Today, TimeZoneInfo.Local).End - 1000 };
        using var coordinator = new Coordinator(clock, new Source(), store, new Log());
        await coordinator.Step(Command.TogglePause);
        var before = await coordinator.Snapshot();
        await coordinator.ChangeTodo(null, Draft(Today), null);
        var id = (await coordinator.Todos(Today)).Items.Single().Id;
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => coordinator.ChangeTodo(id, null, true)));
        Assert.Single((await coordinator.Todos(Today)).Items);
        Assert.Equal(before.State, (await coordinator.Snapshot()).State);
        clock.Now += 2000;
        await Assert.ThrowsAsync<ArgumentException>(() => coordinator.ChangeTodo(id, null, false));
        await Assert.ThrowsAsync<ArgumentException>(() => coordinator.ChangeTodo(null, Draft(Today), null));
        Assert.Empty((await coordinator.Todos(Today.AddDays(1))).Items);
        Assert.Single((await coordinator.DashboardReport(Today, new(2026, 9, 1))).Todos!.Items);
    }

    private sealed class Clock : IClock { public long Now { get; set; } }
    private sealed class Source : IActivitySource { public Observation Observe(long now) => new(now, now, new("blender.exe")); }
    private sealed class Log : ILocalLog { public void Error(string operation, Exception error) => throw new InvalidOperationException(operation, error); }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
