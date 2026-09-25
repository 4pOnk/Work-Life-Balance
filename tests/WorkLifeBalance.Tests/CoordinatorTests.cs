using WorkLifeBalance.Application;
using WorkLifeBalance.Domain;
using WorkLifeBalance.Infrastructure;
using Xunit;

namespace WorkLifeBalance.Tests;

public sealed class CoordinatorTests
{
    [Fact]
    public async Task Failed_transaction_does_not_advance_state_or_emit_notice()
    {
        var clock = new Clock();
        using var store = new Store();
        var log = new Log();
        using var coordinator = new Coordinator(clock, new Source(), store, log);
        var notifications = 0;
        coordinator.StatusChanged += _ => notifications++;
        await coordinator.Step();
        clock.Now = 1000;
        store.Fail = true;
        await coordinator.Step(Command.ToggleAfk);
        Assert.Equal(0, notifications);
        Assert.NotNull((await coordinator.Snapshot()).Error);
        Assert.Equal(0, (await coordinator.Snapshot()).State.At);
        store.Fail = false;
        await coordinator.Step(Command.ToggleAfk);
        Assert.Equal(1, notifications);
        Assert.Equal(Presence.ManualAfk, (await coordinator.Snapshot()).State.Presence);
        Assert.Single(log.Errors);
    }

    [Fact]
    public async Task Concurrent_commands_are_serialized_without_duplicate_intervals()
    {
        var clock = new Clock();
        using var store = new Store();
        using var coordinator = new Coordinator(clock, new Source(), store, new Log());
        await coordinator.Step();
        clock.Now = 1000;
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => coordinator.Step(Command.ToggleAfk)));
        Assert.Equal(Presence.Active, (await coordinator.Snapshot()).State.Presence);
        Assert.Single(store.Transitions, t => t.Slice is not null);
    }

    [Fact]
    public async Task Settings_affect_only_future_classification()
    {
        var clock = new Clock();
        using var store = new Store();
        using var coordinator = new Coordinator(clock, new Source(), store, new Log());
        await coordinator.Step();
        clock.Now = 1000;
        await coordinator.UpdateSettings(new TrackerSettings { WorkProcesses = [] });
        clock.Now = 2000;
        await coordinator.Step();
        var slices = store.Transitions.Select(t => t.Slice).OfType<Slice>().ToArray();
        Assert.Equal(Category.Work, slices[0].Category);
        Assert.Equal(Category.Rest, slices[1].Category);
    }

    private sealed class Clock : IClock { public long Now { get; set; } }
    private sealed class Source : IActivitySource
    {
        public Observation Observe(long now) => new(now, now, new("blender.exe"));
    }
    private sealed class Log : ILocalLog
    {
        public List<Exception> Errors { get; } = [];
        public void Error(string operation, Exception error) => Errors.Add(error);
    }
    private sealed class Store : ITrackerStore
    {
        public bool Fail { get; set; }
        public List<Transition> Transitions { get; } = [];
        private TrackerSettings settings = new();
        public TrackerSettings ReadSettings() => settings;
        public void WriteSettings(TrackerSettings value, long? reclassifyUntil = null) => settings = value;
        public string Correct(IReadOnlyList<TimeRange> ranges, Category category, long now) => throw new NotSupportedException();
        public bool Undo(string actionId) => throw new NotSupportedException();
        public void Apply(Transition transition)
        {
            if (Fail) throw new IOException("Simulated disk failure.");
            Transitions.Add(transition);
        }
        public DayReport ReadDay(DateOnly date, TimeZoneInfo zone) => Reports.Day(date, zone, [], []);
        public void Dispose() { }
    }
}
