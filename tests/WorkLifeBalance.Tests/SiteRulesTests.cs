using WorkLifeBalance.Application;
using WorkLifeBalance.Domain;
using WorkLifeBalance.Infrastructure;
using Xunit;

namespace WorkLifeBalance.Tests;

public sealed class SiteRulesTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "wlb-sites-" + Guid.NewGuid());
    private string Database => Path.Combine(directory, "activity.db");
    private static readonly DateOnly Day = new(2026, 9, 24);
    private static readonly long Start = Reports.Bounds(Day, TimeZoneInfo.Local).Start;
    private static BrowserPage Page(string domain, bool privateWindow = false) => new("https://" + domain + "/docs", domain, "Docs", privateWindow, 1, 1);

    [Theory]
    [InlineData(" Unity.COM ", "unity.com")]
    [InlineData("https://Docs.Unity.com/tutorial?secret=1#part", "docs.unity.com")]
    [InlineData("EXAMPLE.COM.", "example.com")]
    [InlineData("https://пример.рф/", "xn--e1afmkfd.xn--p1ai")]
    [InlineData("127.0.0.1:8080", "127.0.0.1")]
    public void Normalizes_host_without_storing_url_secrets(string input, string host) => Assert.Equal(host, SiteRules.Normalize(input));

    [Theory]
    [InlineData("")]
    [InlineData("bad domain.com")]
    [InlineData("*.example.com")]
    [InlineData("com")]
    [InlineData("ftp://example.com")]
    [InlineData("https://user:secret@example.com")]
    [InlineData("https://example.com\\@evil.com")]
    public void Rejects_invalid_rules(string input) => Assert.Throws<ArgumentException>(() => SiteRules.Normalize(input));

    [Fact]
    public void Matches_host_and_subdomains_but_not_similar_hosts_or_missing_private_pages()
    {
        var settings = new TrackerSettings { WorkSites = ["EXAMPLE.COM", "https://example.com/docs"], WorkProcesses = ["firefox.exe"] }.Validate();
        Assert.Equal(["example.com"], settings.WorkSites);
        foreach (var domain in new[] { "example.com", "docs.example.com", "deep.docs.example.com", "EXAMPLE.COM." })
            Assert.Equal(Category.Work, settings.Classify(new("Firefox.exe", Page(domain))));
        foreach (var domain in new[] { "notexample.com", "example.com.evil.net", "other.net" })
            Assert.Equal(Category.Rest, settings.Classify(new("firefox.exe", Page(domain))));
        Assert.Equal(Category.Rest, settings.Classify(new("firefox.exe", Page("example.com", true))));
        Assert.Equal(Category.Rest, settings.Classify(new("firefox.exe")));
        Assert.Equal(Category.Rest, settings.Classify(new("other.exe", Page("example.com"))));
        Assert.False(SiteRules.Matches("evil.127.0.0.1", ["127.0.0.1"]));
    }

    [Fact]
    public void Old_settings_load_empty_site_rules_and_validation_is_bounded()
    {
        Assert.Empty(System.Text.Json.JsonSerializer.Deserialize<TrackerSettings>("{\"Port\":47831}")!.Validate().WorkSites);
        Assert.Throws<ArgumentException>(() => new TrackerSettings { WorkSites = null! }.Validate());
        Assert.Throws<ArgumentException>(() => new TrackerSettings { WorkSites = Enumerable.Repeat("example.com", 501).ToArray() }.Validate());
    }

    [Fact]
    public void Stored_classification_survives_rule_removal_recalculation_restart_and_manual_undo()
    {
        var state = new TrackerState(Start, Start, new("firefox.exe", Page("example.com")), Category.Work);
        using (var store = new SqliteTrackerStore(Database))
        {
            store.WriteSettings(new() { WorkSites = ["example.com"] });
            store.Apply(new(state, new(Start, Start + 10000, "firefox.exe", Category.Work, Page("example.com")), new(Start, Start + 2000, "AutomaticAfk"), null));
            store.WriteSettings(new() { WorkSites = [], WorkProcesses = [] }, Start + 10000);
            var action = store.Correct([new(Start + 3000, Start + 6000)], Category.Rest, Start + 10000);
            Assert.Equal(5, store.ReadDay(Day, TimeZoneInfo.Local).WorkSeconds);
            Assert.True(store.Undo(action));
        }
        using var reopened = new SqliteTrackerStore(Database);
        Assert.Empty(reopened.ReadSettings().WorkSites);
        var day = reopened.ReadDay(Day, TimeZoneInfo.Local);
        Assert.Equal(8, day.WorkSeconds);
        Assert.Equal(2, day.AfkSeconds);
        Assert.Equal("siteRule", day.Intervals.Last().Source);
    }

    [Fact]
    public async Task Browser_switches_and_settings_changes_apply_at_the_observation_boundary()
    {
        var clock = new Clock { Now = Start };
        using var store = new SqliteTrackerStore(Database);
        store.WriteSettings(new() { WorkSites = ["example.com"] });
        using var coordinator = new Coordinator(clock, new Source(), store, new Log());
        await coordinator.Step();
        var id = Guid.NewGuid();
        await coordinator.BrowserConnection(id, true);
        async Task Observe(long sequence, string host) => Assert.True(await coordinator.BrowserObservation(id,
            new(1, sequence, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), true, false, 1, 1, "https://" + host, "Page")));
        await Observe(1, "example.com");
        Assert.Equal(Category.Work, (await coordinator.Snapshot()).State.Category);
        clock.Now += 1000;
        await Observe(2, "other.net");
        Assert.Equal(Category.Rest, (await coordinator.Snapshot()).State.Category);
        clock.Now += 1000;
        await Observe(3, "example.com");
        clock.Now += 1000;
        await coordinator.UpdateSettings(new() { WorkSites = [] });
        clock.Now += 1000;
        await coordinator.Step();
        var day = store.ReadDay(Day, TimeZoneInfo.Local);
        Assert.Equal(2, day.WorkSeconds);
        Assert.Equal(2, day.RestSeconds);
        await coordinator.UpdateSettings(new() { WorkSites = ["example.com"] });
        Assert.Equal(Category.Work, (await coordinator.Snapshot()).State.Category);
        await coordinator.BrowserConnection(id, false);
        clock.Now += 1000;
        await coordinator.Step();
        Assert.Equal(Category.Rest, (await coordinator.Snapshot()).State.Category);
    }

    private sealed class Clock : IClock { public long Now { get; set; } }
    private sealed class Source : IActivitySource { public Observation Observe(long now) => new(now, now, new("firefox.exe"), 123); }
    private sealed class Log : ILocalLog { public void Error(string operation, Exception error) => throw new InvalidOperationException(operation, error); }
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
