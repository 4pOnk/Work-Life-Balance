using WorkLifeBalance.Domain;
using Xunit;

namespace WorkLifeBalance.Tests;

public sealed class TrackerTests
{
    private static readonly Activity Work = new("blender.exe");
    private static readonly TrackerSettings Settings = new() { AfkMinutes = 0.05 };
    private static TrackerState Start(long now = 0) => Tracker.Start(new(now, now, Work), Settings);
    private static Transition Step(TrackerState state, long now, long input = 0, Command command = Command.Sample,
        TrackerSettings? settings = null) => Tracker.Advance(state, new(now, input, Work), settings ?? Settings, command);

    [Fact]
    public void Afk_retroactively_starts_at_last_input_and_not_at_threshold()
    {
        var state = Start();
        state = Step(state, 1000).State;
        state = Step(state, 2000, 1000).State;
        var before = Step(state, 3999, 1000);
        Assert.Null(before.Away);
        var threshold = Step(before.State, 4000, 1000);
        Assert.Equal(new AwayRange(1000, 4000, "AutomaticAfk"), threshold.Away);
        Assert.Equal(Presence.AutomaticAfk, threshold.State.Presence);
        Assert.NotNull(threshold.Notice);
        Assert.Null(Step(threshold.State, 5000, 1000).Notice);
    }

    [Fact]
    public void Manual_afk_survives_input_until_explicit_exit()
    {
        var manual = Step(Start(), 1000, 1000, Command.ToggleAfk);
        Assert.Equal(Presence.ManualAfk, manual.State.Presence);
        Assert.Null(manual.Away);
        var typing = Step(manual.State, 2000, 2000);
        Assert.Equal(Presence.ManualAfk, typing.State.Presence);
        Assert.Equal(1000, typing.Away!.Start);
        var exit = Step(typing.State, 3000, 0, Command.ToggleAfk);
        Assert.Equal(Presence.Active, exit.State.Presence);
        Assert.Equal(3000, exit.State.IdleFloor);
        Assert.Equal(Presence.Active, Step(exit.State, 4000, 0).State.Presence);
    }

    [Fact]
    public void Automatic_afk_ends_on_real_input_once()
    {
        var afk = Step(Start(), 3000);
        var exit = Step(afk.State, 4000, 3900);
        Assert.Equal(Presence.Active, exit.State.Presence);
        Assert.Equal(4000, exit.Away!.End);
        Assert.NotNull(exit.Notice);
        Assert.Null(Step(exit.State, 5000, 3900).Notice);
    }

    [Fact]
    public void Hotkey_exits_automatic_afk_and_rebases_idle_without_fake_input()
    {
        var afk = Step(Start(), 3000);
        var exit = Step(afk.State, 4000, command: Command.ToggleAfk);
        Assert.Equal(Presence.Active, Step(exit.State, 5000).State.Presence);
        Assert.Equal(4000, Step(Step(exit.State, 5000).State, 7000).Away!.Start);
    }

    [Fact]
    public void Pause_does_not_collect_and_hotkey_does_not_resume()
    {
        var paused = Step(Start(), 1000, command: Command.TogglePause);
        var hotkey = Step(paused.State, 2000, command: Command.ToggleAfk);
        Assert.True(hotkey.State.Paused);
        Assert.Null(hotkey.Slice);
        Assert.True(hotkey.Notice!.Paused);
        var resume = Step(hotkey.State, 4000, command: Command.TogglePause);
        Assert.Null(resume.Slice);
        var sample = Step(resume.State, 5000);
        Assert.Equal(4000, sample.Slice!.Start);
        Assert.Null(sample.Away);
    }

    [Fact]
    public void Sleep_is_known_absence_and_does_not_break_manual_afk()
    {
        var manual = Step(Start(), 1000, command: Command.ToggleAfk);
        var sleep = Step(manual.State, 2000, command: Command.Suspend);
        var resume = Step(sleep.State, 3_600_000, command: Command.Resume);
        Assert.Equal(2000, resume.Slice!.Start);
        Assert.Equal(3_600_000, resume.Away!.End);
        Assert.Equal(Presence.ManualAfk, resume.State.Presence);
        Assert.False(resume.State.Suspended);
    }

    [Fact]
    public void Lock_and_suspend_are_independent()
    {
        var locked = Step(Start(), 1000, command: Command.Lock);
        var sleep = Step(locked.State, 2000, command: Command.Suspend);
        var resume = Step(sleep.State, 10000, command: Command.Resume);
        Assert.Equal(Presence.SystemAfk, resume.State.Presence);
        var unlock = Step(resume.State, 11000, command: Command.Unlock);
        Assert.Equal(Presence.Active, unlock.State.Presence);
        Assert.Equal(11000, unlock.State.IdleFloor);
    }

    [Fact]
    public void Unexplained_gap_is_not_billed_to_foreground_app()
    {
        var gap = Step(Start(), 60_000);
        Assert.Null(gap.Slice);
        Assert.Null(gap.Away);
        Assert.Equal(Presence.Active, gap.State.Presence);
    }

    [Fact]
    public void Negative_clock_delta_never_produces_negative_interval()
    {
        var transition = Step(Start(5000), 4000, 3000);
        Assert.Null(transition.Slice);
        Assert.Equal(5000, transition.State.At);
    }

    [Fact]
    public void Changing_threshold_rechecks_current_idle_but_never_reverses_confirmed_afk()
    {
        var state = Step(Start(), 2000, settings: Settings with { AfkMinutes = 30 }).State;
        var afk = Step(state, 3000);
        Assert.Equal(0, afk.Away!.Start);
        Assert.Equal(Presence.AutomaticAfk, Step(afk.State, 4000, settings: Settings with { AutomaticAfk = false }).State.Presence);
    }

    [Fact]
    public void Firefox_is_rest_even_if_whitelisted_and_names_are_case_insensitive()
    {
        var settings = Settings with { WorkProcesses = ["FIREFOX.exe", "BLENDER.EXE"] };
        Assert.Equal(Category.Rest, settings.Classify(new("firefox.exe")));
        Assert.Equal(Category.Work, settings.Classify(Work));
        Assert.Equal(Category.Rest, settings.Classify(Activity.Unknown));
    }

    [Theory]
    [InlineData("../test.exe")]
    [InlineData("C:\\app.exe")]
    [InlineData("*")]
    public void Settings_reject_paths_and_patterns(string name) =>
        Assert.Throws<ArgumentException>(() => (Settings with { WorkProcesses = [name] }).Validate());
}
