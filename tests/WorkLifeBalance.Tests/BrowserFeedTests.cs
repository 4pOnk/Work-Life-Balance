using WorkLifeBalance.Application;
using WorkLifeBalance.Domain;
using Xunit;

namespace WorkLifeBalance.Tests;

public sealed class BrowserFeedTests
{
    private static readonly TrackerSettings Settings = new();
    private static Observation Foreground(long now = 10000, string executable = "firefox.exe", long handle = 123) => new(now, now, new(executable), handle);
    private static BrowserMessage Message(long sequence = 1, long time = 10000) => new(1, sequence, time, true, false, 7, 9,
        "https://user:password@example.com/docs?q=secret#private", "Documentation");

    [Fact]
    public void Browser_page_requires_fresh_message_and_matching_native_foreground()
    {
        var feed = new BrowserFeed(); var id = Guid.NewGuid(); feed.Connect(id);
        Assert.True(feed.Accept(id, Message(), Foreground(), Settings, 10000));
        Assert.Equal("https://example.com/docs", feed.Enrich(Foreground()).Activity.Browser!.Url);
        Assert.Null(feed.Enrich(Foreground(handle: 456)).Activity.Browser);
        Assert.Null(feed.Enrich(Foreground(executable: "Code.exe")).Activity.Browser);
        Assert.Null(feed.Enrich(Foreground(12501)).Activity.Browser);
        Assert.False(feed.Status(15001).Connected);
    }

    [Fact]
    public void Duplicate_and_old_packets_do_not_refresh_or_replace_page()
    {
        var feed = new BrowserFeed(); var id = Guid.NewGuid(); feed.Connect(id);
        feed.Accept(id, Message(3), Foreground(), Settings, 10000);
        Assert.False(feed.Accept(id, Message(3) with { Title = "duplicate" }, Foreground(12000), Settings, 12000));
        Assert.False(feed.Accept(id, Message(2), Foreground(12000), Settings, 12000));
        Assert.False(feed.Accept(id, Message(4, 0), Foreground(12000), Settings, 12000));
        Assert.Null(feed.Enrich(Foreground(12501)).Activity.Browser);
    }

    [Fact]
    public void Reconnect_retires_old_connection_and_resets_sequence()
    {
        var feed = new BrowserFeed(); var old = Guid.NewGuid(); var current = Guid.NewGuid(); feed.Connect(old);
        feed.Accept(old, Message(), Foreground(), Settings, 10000);
        feed.Connect(current);
        Assert.Null(feed.Enrich(Foreground()).Activity.Browser);
        Assert.False(feed.Accept(old, Message(2), Foreground(), Settings, 10000));
        Assert.True(feed.Accept(current, Message(), Foreground(), Settings, 10000));
        feed.Disconnect(old);
        Assert.NotNull(feed.Enrich(Foreground()).Activity.Browser);
        feed.Disconnect(current);
        Assert.Null(feed.Enrich(Foreground()).Activity.Browser);
    }

    [Fact]
    public void Focus_loss_clears_page_and_background_tabs_never_get_stored()
    {
        var feed = new BrowserFeed(); var id = Guid.NewGuid(); feed.Connect(id);
        feed.Accept(id, Message(), Foreground(), Settings, 10000);
        feed.Accept(id, Message(2) with { Focused = false }, Foreground(), Settings, 10000);
        Assert.Null(feed.Enrich(Foreground()).Activity.Browser);
        feed.Accept(id, Message(3), Foreground(executable: "blender.exe"), Settings, 10000);
        Assert.Null(feed.Enrich(Foreground()).Activity.Browser);
    }

    [Fact]
    public void Private_or_non_web_page_never_keeps_url_or_title()
    {
        var page = BrowserFeed.Sanitize(Message() with { Private = true }, true);
        Assert.Null(page.Url); Assert.Null(page.Title); Assert.Null(page.Domain);
        var internalPage = BrowserFeed.Sanitize(Message() with { Url = "file:///C:/private.txt" }, true);
        Assert.Null(internalPage.Url); Assert.Null(internalPage.Title);
    }

    [Fact]
    public void Domain_only_storage_drops_path_and_credentials()
    {
        var page = BrowserFeed.Sanitize(Message(), false);
        Assert.Equal("https://example.com/", page.Url);
        Assert.Equal("example.com", page.Domain);
    }

    [Theory]
    [InlineData(0, 10000)]
    [InlineData(2, 10000)]
    [InlineData(1, 12001)]
    public void Unsupported_or_future_messages_are_rejected(int version, long time)
    {
        var feed = new BrowserFeed(); var id = Guid.NewGuid(); feed.Connect(id);
        Assert.False(feed.Accept(id, Message(time: time) with { Version = version }, Foreground(), Settings, 10000));
    }
}
