using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using WorkLifeBalance.Domain;
using WorkLifeBalance.Infrastructure;

if (args.Length is 2 or 3 && args[0] == "--seed") { FixtureData.Seed(args[1], args.Length == 3 ? int.Parse(args[2]) : 47831); return; }
if (args.Length == 3 && args[0] == "--browser") { await BrowserSmoke.Run(args[1], args[2]); return; }
if (args.Length != 2) throw new ArgumentException("Usage: Smoke <host.exe> <test-data-directory>");
var executable = Path.GetFullPath(args[0]);
var directory = Path.GetFullPath(args[1]);
Directory.CreateDirectory(directory);
var portProbe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 47831);
try { portProbe.Start(); }
finally { portProbe.Stop(); }
using var client = new HttpClient { BaseAddress = new("http://127.0.0.1:47831"), Timeout = TimeSpan.FromSeconds(3) };
using var host = Launch(directory);
Process? conflict = null;
try
{
    await Wait(async () => { try { return (await client.GetAsync("/api/v1/status")).IsSuccessStatusCode; } catch (HttpRequestException) { return false; } });
    var session = await client.GetFromJsonAsync<JsonElement>("/api/v1/session");
    client.DefaultRequestHeaders.Add("X-WLB-Token", session.GetProperty("token").GetString());
    var settings = new TrackerSettings { AfkMinutes = 30 };
    (await client.PutAsJsonAsync("/api/v1/settings", settings)).EnsureSuccessStatusCode();
    var initial = await Status();
    if (initial.GetProperty("hotkeyError").ValueKind != JsonValueKind.Null) throw new Exception("Preferred hotkey unavailable.");
    if (State(initial).GetProperty("activity").GetProperty("executable").GetString() == "WorkLifeBalance.Host.exe")
        throw new Exception("Expected another application in foreground.");

    Native.SendPreferredHotkey();
    await Wait(async () => State(await Status()).GetProperty("presence").GetString() == "manualAfk");
    Native.SendPreferredHotkey();
    await Wait(async () => State(await Status()).GetProperty("presence").GetString() == "active");
    Console.WriteLine("PASS global Win+Shift+N delivery while another application is foreground");

    (await client.PutAsJsonAsync("/api/v1/settings", settings with { AfkMinutes = 0.05 })).EnsureSuccessStatusCode();
    var observedIdle = true;
    uint largestIdle = 0;
    try
    {
        await Wait(async () =>
    {
        largestIdle = Math.Max(largestIdle, Native.IdleMilliseconds());
        return State(await Status()).GetProperty("presence").GetString() == "automaticAfk";
    });
    }
    catch (TimeoutException) when (largestIdle < 4500)
    {
        observedIdle = false;
        Console.WriteLine($"SKIP live idle timeout: largest observed input gap was {largestIdle} ms (needs three seconds plus a collector tick).");
    }
    if (observedIdle)
    {
        var afk = await Status();
        var since = State(afk).GetProperty("awaySince").GetInt64();
        var now = afk.GetProperty("tracker").GetProperty("serverTime").GetInt64();
        if (now - since < 3000) throw new Exception("AFK did not backdate to the idle beginning.");
        Native.SendPreferredHotkey();
        await Wait(async () => State(await Status()).GetProperty("presence").GetString() == "active");
        Console.WriteLine("PASS real last-input timeout and retroactive AFK; hotkey return");
    }
    (await client.PutAsJsonAsync("/api/v1/settings", settings)).EnsureSuccessStatusCode();

    // A competing instance has a different data identity, but requests the same TCP port and hotkey.
    var conflictDirectory = Path.Combine(directory, "port-conflict");
    conflict = Launch(conflictDirectory);
    var errorPath = Path.Combine(conflictDirectory, "errors.log");
    await Wait(() => Task.FromResult(File.Exists(errorPath) && File.ReadAllText(errorPath).Contains("http-start")));
    if (conflict.HasExited) throw new Exception("A port conflict stopped background tracking.");
    await Task.Delay(1200);
    using (var store = new SqliteTrackerStore(Path.Combine(conflictDirectory, "activity.db")))
    {
        var day = store.ReadDay(DateOnly.FromDateTime(DateTime.Now), TimeZoneInfo.Local);
        if (day.Intervals.Count == 0) throw new Exception("No tracking during port conflict.");
    }
    Console.WriteLine("PASS port conflict is reported and background collection continues");
    await Shutdown(conflictDirectory, conflict);
    conflict.Dispose(); conflict = null;

    var before = await Status();
    var beforeSeconds = Total(before);
    host.Kill();
    await host.WaitForExitAsync();
    await Task.Delay(1800);
    using var restarted = Launch(directory);
    await Wait(async () => { try { return (await client.GetAsync("/api/v1/status")).IsSuccessStatusCode; } catch (HttpRequestException) { return false; } });
    var recovered = await Status();
    if (Total(recovered) < beforeSeconds || Total(recovered) > beforeSeconds + 1.5)
        throw new Exception("Recovery lost checkpoint data or billed downtime.");
    var token = await client.GetFromJsonAsync<JsonElement>("/api/v1/session");
    client.DefaultRequestHeaders.Remove("X-WLB-Token");
    client.DefaultRequestHeaders.Add("X-WLB-Token", token.GetProperty("token").GetString());
    await Shutdown(directory, restarted);
    Console.WriteLine("PASS crash recovery preserves checkpoint and excludes downtime; IPC shutdown works");
}
finally
{
    if (conflict is not null) { if (!conflict.HasExited) conflict.Kill(); conflict.Dispose(); }
    if (!host.HasExited) await Shutdown(directory, host);
}

Process Launch(string dataDirectory)
{
    var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
    info.ArgumentList.Add("--data-dir"); info.ArgumentList.Add(dataDirectory);
    return Process.Start(info) ?? throw new Exception("Host did not start.");
}
async Task Shutdown(string dataDirectory, Process target)
{
    var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
    info.ArgumentList.Add("--data-dir"); info.ArgumentList.Add(dataDirectory); info.ArgumentList.Add("--shutdown");
    using var command = Process.Start(info)!;
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    await command.WaitForExitAsync(timeout.Token);
    await target.WaitForExitAsync(timeout.Token);
}
async Task<JsonElement> Status() => await client.GetFromJsonAsync<JsonElement>("/api/v1/status");
static JsonElement State(JsonElement value) => value.GetProperty("tracker").GetProperty("state");
static double Total(JsonElement value)
{
    var day = value.GetProperty("tracker").GetProperty("today");
    return day.GetProperty("workSeconds").GetDouble() + day.GetProperty("restSeconds").GetDouble() + day.GetProperty("afkSeconds").GetDouble();
}
static async Task Wait(Func<Task<bool>> predicate)
{
    var started = Stopwatch.StartNew();
    while (started.Elapsed < TimeSpan.FromSeconds(12))
    {
        if (await predicate()) return;
        await Task.Delay(100);
    }
    throw new TimeoutException("Native smoke condition was not met.");
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size; public uint Tick; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInput value);
    public static uint IdleMilliseconds()
    {
        var value = new LastInput { Size = 8 };
        if (!GetLastInputInfo(ref value)) throw new Exception("Cannot read Windows idle time.");
        return unchecked((uint)Environment.TickCount64 - value.Tick);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Union; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct InputUnion { [FieldOffset(0)] public Keyboard Keyboard; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Keyboard
    { public ushort Key; public ushort Scan; public uint Flags; public uint Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] input, int size);

    public static void SendPreferredHotkey()
    {
        static Input Key(ushort key, bool up = false) => new()
        {
            Type = 1,
            Union = new() { Keyboard = new() { Key = key, Flags = up ? 2u : 0u } }
        };
        Input[] inputs = [Key(0x5B), Key(0x10), Key(0x4E), Key(0x4E, true), Key(0x10, true), Key(0x5B, true)];
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
}
