namespace WorkLifeBalance.Domain;

public enum Category { Work, Rest, Afk }
public enum Presence { Active, AutomaticAfk, ManualAfk, SystemAfk }
public enum Command { Sample, ToggleAfk, TogglePause, Lock, Unlock, Suspend, Resume, Stop }
public sealed record BrowserPage(string? Url, string? Domain, string? Title, bool Private, int WindowId, int TabId);
public sealed record Activity(string Executable, BrowserPage? Browser = null)
{
    public static Activity Unknown { get; } = new("unknown");
}

public sealed record TrackerSettings
{
    public int Port { get; init; } = 47831;
    public bool AutomaticAfk { get; init; } = true;
    public double AfkMinutes { get; init; } = 30;
    public string Hotkey { get; init; } = "Win+Shift+N";
    public bool BrowserPath { get; init; } = true;
    public int RetentionDays { get; init; }
    public string[] WorkProcesses { get; init; } = ["blender.exe", "Unity.exe", "devenv.exe", "Code.exe"];
    public string[] WorkSites { get; init; } = [];

    public TrackerSettings Validate()
    {
        if (RetentionDays is < 0 or > 36500) throw new ArgumentException("Срок хранения: от 1 до 36500 дней или 0 без ограничения.");
        if (Port is < 1024 or > 65535) throw new ArgumentException("Порт должен быть от 1024 до 65535.");
        if (!double.IsFinite(AfkMinutes) || AfkMinutes < 0.05 || AfkMinutes > 10080)
            throw new ArgumentException("Порог AFK: от 0,05 до 10080 минут.");
        if (WorkProcesses is null || WorkProcesses.Length > 500)
            throw new ArgumentException("Допускается не более 500 рабочих процессов.");
        if (WorkSites is null || WorkSites.Length > 500)
            throw new ArgumentException("Допускается не более 500 рабочих сайтов.");
        var sites = WorkSites.Select(SiteRules.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var names = WorkProcesses.Select(name => name?.Trim() ?? "").ToArray();
        if (names.Any(name => name.Length > 128 || !System.Text.RegularExpressions.Regex.IsMatch(name,
            @"^[\p{L}\p{N}_ .()\-]+\.exe$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            throw new ArgumentException("Укажите имя приложения с расширением .exe, без пути.");
        if (string.IsNullOrWhiteSpace(Hotkey) || Hotkey.Length > 80)
            throw new ArgumentException("Не задано сочетание клавиш.");
        return this with { WorkProcesses = names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), WorkSites = sites };
    }

    public Category Classify(Activity activity)
    {
        if (activity.Executable.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase))
            return activity.Browser is { Private: false } page && SiteRules.Matches(page.Domain, WorkSites)
                ? Category.Work : Category.Rest;
        return WorkProcesses.Contains(activity.Executable, StringComparer.OrdinalIgnoreCase) ? Category.Work : Category.Rest;
    }
}

// All persisted intervals are half-open [Start, End), in UTC milliseconds.
public sealed record Slice(long Start, long End, string Executable, Category Category,
    BrowserPage? Browser = null, string Source = "appRule", string? CorrectionId = null);
public sealed record AwayRange(long Start, long End, string Reason);
public sealed record Observation(long Now, long LastInput, Activity Activity, long WindowHandle = 0);
public sealed record TimeRange(long Start, long End);
public sealed record Correction(long Id, string ActionId, long Start, long End, Category Category);
public sealed record RuleRevision(long Id, long Until, string[] WorkProcesses);
public sealed record StatusChange(Presence Presence, bool Paused, long Since, string? Message = null);
public sealed record TrackerState(long At, long IdleFloor, Activity Activity, Category Category)
{
    public bool Paused { get; init; }
    public bool Manual { get; init; }
    public bool Locked { get; init; }
    public bool Suspended { get; init; }
    public long? AutoSince { get; init; }
    public long? AwaySince { get; init; }
    public Presence Presence => Manual ? Presence.ManualAfk : Locked || Suspended ? Presence.SystemAfk :
        AutoSince.HasValue ? Presence.AutomaticAfk : Presence.Active;
}
public sealed record Transition(TrackerState State, Slice? Slice, AwayRange? Away, StatusChange? Notice, TimeRange? Pause = null);
