using WorkLifeBalance.Domain;
using WorkLifeBalance.Infrastructure;

internal static class FixtureData
{
    public static void Seed(string directory, int port = 47831)
    {
        var path = Path.Combine(Path.GetFullPath(directory), "activity.db");
        if (File.Exists(path)) throw new InvalidOperationException("Fixtures require a new test database.");
        using var store = new SqliteTrackerStore(path);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var yesterday = today.AddDays(-1);
        var completed = store.CreateTodo(new("Тест: вчерашняя задача", 3, yesterday.ToString("yyyy-MM-dd")), yesterday);
        store.CompleteTodo(completed.Id, true, yesterday, Reports.Bounds(yesterday, TimeZoneInfo.Local).Start + 3600000);
        store.CreateTodo(new("Тест: перенесённая задача", 2, yesterday.ToString("yyyy-MM-dd")), yesterday);
        store.WriteSettings(new TrackerSettings { Port = port, Hotkey = port == 47831 ? "Win+Shift+N" : "Ctrl+Shift+F11" });
        var start = DateTimeOffset.Now.AddMinutes(-12).ToUnixTimeMilliseconds();
        var state = new TrackerState(start, start, new("firefox.exe"), Category.Rest);
        store.Apply(new(state, new(start, start + 120000, "firefox.exe", Category.Rest,
            new("https://example.com/learning", "example.com", "Тестовый урок: геометрия", false, 1, 1)), null, null));
        store.Apply(new(state, new(start + 120000, start + 180000, "blender.exe", Category.Work), null, null));
        store.Apply(new(state, new(start + 180000, start + 240000, "firefox.exe", Category.Rest,
            new("https://example.com/video", "example.com", "Тестовое видео", false, 1, 2)), null, null));
        store.Apply(new(state, new(start + 240000, start + 300000, "Code.exe", Category.Work),
            new(start + 240000, start + 300000, "AutomaticAfk"), null));
        Console.WriteLine("Created explicitly synthetic browser fixtures in the test database.");
    }
}
