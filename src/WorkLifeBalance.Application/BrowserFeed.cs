using WorkLifeBalance.Domain;

namespace WorkLifeBalance.Application;

public sealed record BrowserMessage(int Version, long Sequence, long ObservedAt, bool Focused,
    bool Private, int WindowId, int TabId, string? Url, string? Title);
public sealed record BrowserStatus(bool Connected, long? LastSeen, string State);

// Accessed only while the coordinator owns its gate. Messages never allocate tracked time.
public sealed class BrowserFeed
{
    private Guid connection;
    private long sequence = -1;
    private long seen;
    private long windowHandle;
    private BrowserPage? page;
    private bool connected;

    public void Connect(Guid id) { connection = id; sequence = -1; page = null; connected = true; seen = 0; }
    public void Disconnect(Guid id) { if (connection == id) { connected = false; page = null; } }
    public bool Accept(Guid id, BrowserMessage message, Observation native, TrackerSettings settings, long wallNow)
    {
        if (!connected || connection != id) return false;
        if (message.Version != 1 || message.Sequence < 0 || message.Sequence <= sequence ||
            message.ObservedAt < wallNow - 5000 || message.ObservedAt > wallNow + 2000) return false;
        if (message.Url?.Length > 8192 || message.Title?.Length > 1024) return false;
        sequence = message.Sequence;
        seen = native.Now;
        page = null;
        windowHandle = native.WindowHandle;
        if (!message.Focused || !native.Activity.Executable.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase)) return true;
        page = Sanitize(message, settings.BrowserPath);
        return true;
    }
    public Observation Enrich(Observation native)
    {
        if (!connected || native.Now - seen > 2500 || native.WindowHandle != windowHandle ||
            !native.Activity.Executable.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase)) return native;
        return native with { Activity = native.Activity with { Browser = page } };
    }
    public BrowserStatus Status(long now) => new(connected && seen > 0 && now - seen <= 5000, seen > 0 ? seen : null,
        !connected ? "disconnected" : seen == 0 ? "connecting" : now - seen > 5000 ? "stale" : "connected");
    public void ClearPage() => page = null;

    public static BrowserPage Sanitize(BrowserMessage message, bool includePath)
    {
        if (message.Private) return new(null, null, null, true, message.WindowId, message.TabId);
        if (!Uri.TryCreate(message.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return new(null, null, null, false, message.WindowId, message.TabId);
        var clean = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" };
        if (!includePath) clean.Path = "/";
        var title = string.IsNullOrWhiteSpace(message.Title) ? null :
            new string(message.Title.Where(c => !char.IsControl(c)).Take(512).ToArray());
        return new(clean.Uri.AbsoluteUri, uri.IdnHost, title, false, message.WindowId, message.TabId);
    }
}
