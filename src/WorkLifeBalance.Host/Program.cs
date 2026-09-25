using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using WorkLifeBalance.Application;
using WorkLifeBalance.Domain;
using WorkLifeBalance.Infrastructure;
using WorkLifeBalance.Windows;

namespace WorkLifeBalance.Host;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        var dataIndex = Array.IndexOf(args, "--data-dir");
        var dataDirectory = dataIndex >= 0 && dataIndex + 1 < args.Length ? Path.GetFullPath(args[dataIndex + 1]) :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkLifeBalance");
        var log = new LocalLog(dataDirectory);
        var identity = NativeProtocol.Identity(dataDirectory);
        using var mutex = new Mutex(true, @"Local\WorkLifeBalance-" + identity, out var first);
        if (!first) { SignalExisting(identity, args.Contains("--shutdown") ? (byte)2 : (byte)1); return; }
        if (args.Contains("--shutdown")) { mutex.ReleaseMutex(); return; }
        try { Run(dataDirectory, identity, log, args.Contains("--open")); }
        catch (Exception e)
        {
            log.Error("startup", e);
            MessageBox.Show("Не удалось запустить Work Life Balance. Данные не удалены.\n" +
                "Проверьте журнал: " + Path.Combine(dataDirectory, "errors.log"), "Work Life Balance",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static void Run(string dataDirectory, string identity, LocalLog log, bool open)
    {
        using var store = new SqliteTrackerStore(Path.Combine(dataDirectory, "activity.db"));
        var source = new WindowsActivitySource();
        using var coordinator = new Coordinator(new MonotonicClock(), source, store, log);
        using var stop = new CancellationTokenSource();
        var settings = store.ReadSettings();
        Hotkey.Parse(settings.Hotkey);
        coordinator.Step(WindowsActivitySource.IsSessionLocked() ? Command.Lock : Command.Sample).GetAwaiter().GetResult();
        using var tray = new TrayContext(coordinator, log, settings, stop.Cancel);
        var tracking = Task.Run(() => coordinator.Run(stop.Token));
        var browserTracking = Task.Run(() => new BrowserPipeServer(coordinator, identity, settings.Port, log).Run(stop.Token));
        var server = BuildServer(coordinator, source, tray, settings.Port, log, dataDirectory);
        try { server.StartAsync(stop.Token).GetAwaiter().GetResult(); }
        catch (Exception e)
        {
            log.Error("http-start", e);
            tray.ServerError = $"Веб-интерфейс недоступен на порту {settings.Port}. Возможно, порт занят. Фоновый учёт продолжает работать.";
            tray.Notify(tray.ServerError);
        }
        var listener = Listen(identity, tray, stop.Token, log);
        if (open && tray.ServerError is null) tray.Open();
        try { System.Windows.Forms.Application.Run(tray); }
        finally
        {
            stop.Cancel();
            Task.WhenAll(tracking, listener, browserTracking).GetAwaiter().GetResult();
            coordinator.Step(Command.Stop).GetAwaiter().GetResult();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            server.StopAsync(timeout.Token).GetAwaiter().GetResult();
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static WebApplication BuildServer(Coordinator coordinator, IProcessCatalog catalog, TrayContext tray, int port, ILocalLog log, string dataDirectory)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Args = []
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(System.Net.IPAddress.Loopback, port, endpoint => endpoint.Protocols = HttpProtocols.Http1);
            options.Limits.MaxRequestBodySize = 32_768;
        });
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
        var app = builder.Build();
        var origin = $"http://127.0.0.1:{port}";
        var csrf = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var startup = new StartupRegistration(Environment.ProcessPath!, dataDirectory);
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            context.Response.Headers.CacheControl = "no-store";
            var request = context.Request;
            if (request.Host.Value != $"127.0.0.1:{port}" ||
                (request.Headers.Origin.Count > 0 && request.Headers.Origin != origin) ||
                request.Headers["Sec-Fetch-Site"] == "cross-site")
            {
                context.Response.StatusCode = 403;
                return;
            }
            if (request.Method is not ("GET" or "HEAD") && request.Headers["X-WLB-Token"] != csrf)
            {
                context.Response.StatusCode = 403;
                return;
            }
            try { await next(context); }
            catch (ArgumentException e)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = e.Message });
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { /* Client disconnected. */ }
            catch (Exception e)
            {
                log.Error("http", e);
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new { error = "Локальная операция не выполнена. Проверьте журнал ошибок." });
            }
        });
        app.MapGet("/api/v1/session", () => new { token = csrf });
        app.MapGet("/api/v1/status", async (CancellationToken cancellation) => new
        {
            tracker = await coordinator.Snapshot(cancellation),
            tray.HotkeyError,
            activePort = port,
            firefox = await coordinator.BrowserStatus(cancellation)
        });
        app.MapGet("/api/v1/processes", () => catalog.List());
        app.MapGet("/api/v1/startup", () => new { enabled = startup.Enabled });
        app.MapPut("/api/v1/startup", (StartupRequest request) =>
        {
            startup.Set(request.Enabled);
            return Results.Ok(new { enabled = startup.Enabled });
        });
        app.MapGet("/api/v1/storage", async (CancellationToken cancellation) => await coordinator.Storage(cancellation));
        app.MapPost("/api/v1/storage/backup", async (CancellationToken cancellation) =>
            await coordinator.ManageHistory("backup", null, null, cancellation));
        app.MapPost("/api/v1/storage/restore", async (RestoreRequest request, CancellationToken cancellation) =>
        {
            if (request.Confirmation != "ВОССТАНОВИТЬ") throw new ArgumentException("Подтвердите замену истории словом ВОССТАНОВИТЬ.");
            return await coordinator.ManageHistory("restore", null, request.Id, cancellation);
        });
        app.MapPost("/api/v1/storage/delete", async (DeleteRequest request, CancellationToken cancellation) =>
        {
            if (request.Confirmation != "УДАЛИТЬ") throw new ArgumentException("Подтвердите удаление словом УДАЛИТЬ.");
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            TimeRange range;
            if (request.All) range = new(0, now);
            else
            {
                if (!DateOnly.TryParseExact(request.From, "yyyy-MM-dd", out var from) || from.Year < 1970 ||
                    !DateOnly.TryParseExact(request.To, "yyyy-MM-dd", out var to) || to.Year > 9998 || to < from)
                    throw new ArgumentException("Неверный диапазон удаления.");
                range = new(Reports.Bounds(from, TimeZoneInfo.Local).Start, Math.Min(now, Reports.Bounds(to, TimeZoneInfo.Local).End));
            }
            return await coordinator.ManageHistory("delete", range, null, cancellation);
        });
        app.MapGet("/api/v1/export", async (string from, string to, string format, CancellationToken cancellation) =>
        {
            if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", out var first) || !DateOnly.TryParseExact(to, "yyyy-MM-dd", out var last) ||
                format is not ("csv" or "json")) throw new ArgumentException("Неверный диапазон или формат экспорта.");
            var report = await coordinator.Export(first, last, cancellation);
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            var text = format == "csv" ? "\uFEFF" + ExportFormat.Csv(report) : JsonSerializer.Serialize(report, options);
            return Results.File(Encoding.UTF8.GetBytes(text), format == "csv" ? "text/csv; charset=utf-8" : "application/json",
                $"work-life-balance-{first:yyyy-MM-dd}-{last:yyyy-MM-dd}.{format}");
        });
        app.MapGet("/api/v1/firefox/package", () =>
        {
            var package = Path.Combine(AppContext.BaseDirectory, "firefox-extension.zip");
            return File.Exists(package) ? Results.File(package, "application/zip", "work-life-balance-firefox-0.2.0-unsigned.zip") : Results.NotFound();
        });
        app.MapPut("/api/v1/settings", async (TrackerSettings value, string? scope, CancellationToken cancellation) =>
        {
            value = value.Validate();
            Hotkey.Parse(value.Hotkey);
            var previous = await coordinator.GetSettings(cancellation);
            if (scope is not (null or "future" or "history")) throw new ArgumentException("Неверная область применения правил.");
            await coordinator.UpdateSettings(value, cancellation, scope == "history");
            if (previous.Hotkey != value.Hotkey) await tray.ConfigureHotkey(value.Hotkey);
            return Results.Ok(new { settings = await coordinator.GetSettings(cancellation), tray.HotkeyError, restartRequired = value.Port != port });
        });
        app.MapGet("/api/v1/dashboard", async (string date, string month, CancellationToken cancellation) =>
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var day) || day.Year is < 1970 or > 9998 ||
                !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out var first) || first.Year is < 1970 or > 9998)
                throw new ArgumentException("Неверная дата или месяц отчёта.");
            return await coordinator.DashboardReport(day, first, cancellation);
        });
        app.MapGet("/api/v1/todos", async (string date, CancellationToken cancellation) =>
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var day) || day.Year is < 1970 or > 9998)
                throw new ArgumentException("Неверная дата.");
            return await coordinator.Todos(day, cancellation);
        });
        app.MapPost("/api/v1/todos", async (TodoDraft draft, CancellationToken cancellation) =>
        {
            await coordinator.ChangeTodo(null, draft, null, cancellation);
            return Results.Ok();
        });
        app.MapPut("/api/v1/todos/{id}", async (string id, TodoDraft draft, CancellationToken cancellation) =>
        {
            await coordinator.ChangeTodo(id, draft, null, cancellation);
            return Results.Ok();
        });
        app.MapPost("/api/v1/todos/{id}/completion", async (string id, TodoCompletion request, CancellationToken cancellation) =>
        {
            await coordinator.ChangeTodo(id, null, request.Completed, cancellation);
            return Results.Ok();
        });
        app.MapDelete("/api/v1/todos/{id}", async (string id, CancellationToken cancellation) =>
        {
            await coordinator.ChangeTodo(id, null, null, cancellation);
            return Results.Ok();
        });
        app.MapGet("/api/v1/history", async (string date, CancellationToken cancellation) =>
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var day)) throw new ArgumentException("Неверная дата.");
            return await coordinator.History(day, cancellation);
        });
        app.MapPost("/api/v1/corrections", async (CorrectionRequest request, CancellationToken cancellation) =>
            Results.Ok(new { actionId = await coordinator.Correct(request.Ranges, request.Category, cancellation) }));
        app.MapDelete("/api/v1/corrections/{actionId}", async (string actionId, CancellationToken cancellation) =>
            await coordinator.Undo(actionId, cancellation) ? Results.Ok() : Results.NotFound());
        app.MapPost("/api/v1/afk", async (CancellationToken cancellation) =>
        {
            await coordinator.Step(Command.ToggleAfk, cancellation);
            return Results.Ok();
        });
        app.MapPost("/api/v1/pause", async (CancellationToken cancellation) =>
        {
            await coordinator.Step(Command.TogglePause, cancellation);
            return Results.Ok();
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapFallbackToFile("index.html");
        return app;
    }

    private sealed record CorrectionRequest(TimeRange[] Ranges, Category Category);
    private sealed record TodoCompletion(bool Completed);
    private sealed record StartupRequest(bool Enabled);
    private sealed record RestoreRequest(string Id, string Confirmation);
    private sealed record DeleteRequest(string? From, string? To, bool All, string Confirmation);

    private static void SignalExisting(string identity, byte command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", "WorkLifeBalance-" + identity, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(3000);
            pipe.WriteByte(command);
        }
        catch (IOException) { MessageBox.Show("Приложение уже запускается. Откройте его через значок в трее.", "Work Life Balance"); }
        catch (TimeoutException) { MessageBox.Show("Приложение уже работает. Откройте его через значок в трее.", "Work Life Balance"); }
    }

    private static async Task Listen(string identity, TrayContext tray, CancellationToken cancellation, ILocalLog log)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await using var pipe = new NamedPipeServerStream("WorkLifeBalance-" + identity, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation).ConfigureAwait(false);
                var buffer = new byte[1];
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    if (await pipe.ReadAsync(buffer, timeout.Token).ConfigureAwait(false) == 1)
                    {
                        if (buffer[0] == 1) tray.RequestOpen();
                        if (buffer[0] == 2) tray.RequestExit();
                    }
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { /* Incomplete local client. */ }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { /* Normal shutdown. */ }
        catch (IOException e) { log.Error("single-instance", e); }
    }
}
