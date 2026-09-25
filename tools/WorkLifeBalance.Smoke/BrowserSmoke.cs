using System.Diagnostics;
using System.Text.Json;
using WorkLifeBalance.Application;
using WorkLifeBalance.Domain;
using WorkLifeBalance.Infrastructure;

internal static class BrowserSmoke
{
    public static async Task Run(string executable, string directory)
    {
        directory = Path.GetFullPath(directory);
        using var store = new SqliteTrackerStore(Path.Combine(directory, "activity.db"));
        var clock = new Clock { Now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        var source = new Source(); var log = new LocalLog(directory);
        using var coordinator = new Coordinator(clock, source, store, log);
        using var stop = new CancellationTokenSource();
        var server = new BrowserPipeServer(coordinator, NativeProtocol.Identity(directory), 47831, log).Run(stop.Token);
        var info = new ProcessStartInfo(Path.GetFullPath(executable))
        { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        info.ArgumentList.Add("--data-dir"); info.ArgumentList.Add(directory);
        using var bridge = Process.Start(info)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await coordinator.Step();
            var packet = new BrowserMessage(1, 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), true, false, 7, 8,
                "https://user:secret@example.com/lesson?private=secret#token", "Synthetic bridge lesson");
            var accepted = await Send(packet);
            if (!accepted.GetProperty("ok").GetBoolean()) throw new Exception("Native bridge rejected the packet.");
            clock.Now += 1000; await coordinator.Step();
            var report = (await coordinator.Snapshot()).Today;
            if (!report.Intervals.Any(s => s.Browser?.Url == "https://example.com/lesson")) throw new Exception("Browser page did not reach SQLite.");
            if ((await Send(packet)).GetProperty("ok").GetBoolean()) throw new Exception("Duplicate packet accepted.");
            source.Executable = "blender.exe"; clock.Now += 1000; await coordinator.Step();
            clock.Now += 1000; await coordinator.Step();
            if ((await coordinator.Snapshot()).State.Activity.Browser is not null) throw new Exception("Background browser metadata leaked into another app.");
            bridge.StandardInput.Close();
            await bridge.WaitForExitAsync(timeout.Token);
            source.Executable = "firefox.exe"; clock.Now += 3000; await coordinator.Step();
            if ((await coordinator.Snapshot()).State.Activity.Browser is not null) throw new Exception("Disconnected browser retained metadata.");
            Console.WriteLine("PASS native stdio -> current-user pipe -> coordinator -> SQLite, redaction, duplicate rejection and disconnect fallback");
        }
        finally
        {
            if (!bridge.HasExited) { bridge.Kill(); await bridge.WaitForExitAsync(); }
            stop.Cancel(); await server;
        }
        async Task<JsonElement> Send(BrowserMessage message)
        {
            await NativeProtocol.Write(bridge.StandardInput.BaseStream, JsonSerializer.SerializeToUtf8Bytes(message, NativeProtocol.Json), timeout.Token);
            var bytes = await NativeProtocol.Read(bridge.StandardOutput.BaseStream, timeout.Token) ?? throw new EndOfStreamException();
            return JsonSerializer.Deserialize<JsonElement>(bytes);
        }
    }
    private sealed class Clock : IClock { public long Now { get; set; } }
    private sealed class Source : IActivitySource
    {
        public string Executable { get; set; } = "firefox.exe";
        public Observation Observe(long now) => new(now, now, new(Executable), Executable == "firefox.exe" ? 123 : 456);
    }
}
