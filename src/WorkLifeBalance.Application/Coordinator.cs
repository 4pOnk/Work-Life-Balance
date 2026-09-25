using WorkLifeBalance.Domain;

namespace WorkLifeBalance.Application;

public sealed class Coordinator(IClock clock, IActivitySource source, ITrackerStore store, ILocalLog log) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private TrackerSettings settings = store.ReadSettings();
    private TrackerState? state;
    private string? error;
    private readonly BrowserFeed browser = new();
    public event Action<StatusChange>? StatusChanged;

    public async Task Step(Command command = Command.Sample, CancellationToken cancellation = default)
    {
        StatusChange? notice = null;
        await gate.WaitAsync(cancellation);
        try
        {
            var observation = browser.Enrich(source.Observe(clock.Now));
            state ??= Tracker.Start(observation, settings);
            var transition = Tracker.Advance(state, observation, settings, command);
            store.Apply(transition);
            state = transition.State;
            error = null;
            notice = transition.Notice;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (error is null) log.Error("tracker", exception);
            error = "Не удалось обновить учёт. Проверьте доступ к локальной базе и журнал ошибок.";
        }
        finally { gate.Release(); }
        if (notice is not null) StatusChanged?.Invoke(notice);
    }

    public async Task Run(CancellationToken cancellation)
    {
        await Step(cancellation: cancellation);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellation)) await Step(cancellation: cancellation);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { /* Normal shutdown. */ }
    }

    public async Task<TrackerSnapshot> Snapshot(CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            var now = clock.Now;
            var current = state ?? Tracker.Start(source.Observe(now), settings);
            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(now), TimeZoneInfo.Local).DateTime);
            return new(settings, current, store.ReadDay(day, TimeZoneInfo.Local), error, now);
        }
        finally { gate.Release(); }
    }

    public async Task<TrackerSettings> GetSettings(CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try { return settings with { WorkProcesses = [.. settings.WorkProcesses], WorkSites = [.. settings.WorkSites] }; }
        finally { gate.Release(); }
    }

    public async Task UpdateSettings(TrackerSettings value, CancellationToken cancellation = default, bool reclassify = false)
    {
        value = value.Validate();
        await gate.WaitAsync(cancellation);
        try
        {
            // Close the current slice with the old rules before changing future classification.
            var observation = browser.Enrich(source.Observe(clock.Now));
            state ??= Tracker.Start(observation, settings);
            var transition = Tracker.Advance(state, observation, settings, Command.Stop);
            store.Apply(transition);
            state = transition.State;
            store.WriteSettings(value, reclassify ? observation.Now : null);
            if (value.BrowserPath != settings.BrowserPath) browser.ClearPage();
            settings = value;
            state = state with { Category = settings.Classify(state.Activity) };
        }
        finally { gate.Release(); }
        await Step(cancellation: cancellation);
    }

    public async Task<DayReport> History(DateOnly date, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try { return store.ReadDay(date, TimeZoneInfo.Local); }
        finally { gate.Release(); }
    }

    public async Task<string> Correct(IReadOnlyList<TimeRange> ranges, Category category, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try { return store.Correct(ranges, category, clock.Now); }
        finally { gate.Release(); }
    }

    public async Task<DashboardReport> DashboardReport(DateOnly date, DateOnly month, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            var zone = TimeZoneInfo.Local;
            var now = clock.Now;
            var reports = new List<DayReport>();
            for (var i = 1; i <= DateTime.DaysInMonth(month.Year, month.Month); i++)
            {
                cancellation.ThrowIfCancellationRequested();
                reports.Add(store.ReadDay(new(month.Year, month.Month, i), zone));
            }
            var selected = reports.FirstOrDefault(x => x.Date == date.ToString("yyyy-MM-dd")) ?? store.ReadDay(date, zone);
            long? since = null;
            if (settings.AutomaticAfk && state is { Paused: false, Presence: Presence.Active })
                since = Math.Max(state.IdleFloor, source.Observe(now).LastInput);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(now), zone).DateTime);
            return new(Dashboard.Detail(selected, zone, now, since), Dashboard.Month(month, zone, reports, now),
                (store as ITodoStore)?.ReadTodos(date, today));
        }
        finally { gate.Release(); }
    }

    public async Task<TodoDay> Todos(DateOnly date, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try { return ((ITodoStore)store).ReadTodos(date, LocalToday(clock.Now)); }
        finally { gate.Release(); }
    }

    private static DateOnly LocalToday(long now) => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(now), TimeZoneInfo.Local).DateTime);

    public async Task ChangeTodo(string? id, TodoDraft? draft, bool? completed, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            var todos = (ITodoStore)store;
            var now = clock.Now;
            var today = LocalToday(now);
            if (id is null && draft is not null) todos.CreateTodo(draft, today);
            else if (id is not null && draft is not null) todos.UpdateTodo(id, draft, today);
            else if (id is not null && completed.HasValue) todos.CompleteTodo(id, completed.Value, today, now);
            else if (id is not null) todos.DeleteTodo(id, today);
            else throw new ArgumentException("Неверное действие с задачей.");
        }
        finally { gate.Release(); }
    }

    public async Task<bool> Undo(string actionId, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try { return store.Undo(actionId); }
        finally { gate.Release(); }
    }

    public async Task<BrowserStatus> BrowserStatus(CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try { return browser.Status(clock.Now); }
        finally { gate.Release(); }
    }

    public async Task BrowserConnection(Guid id, bool connect, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try { if (connect) browser.Connect(id); else browser.Disconnect(id); }
        finally { gate.Release(); }
    }

    public async Task<bool> BrowserObservation(Guid id, BrowserMessage message, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            var native = source.Observe(clock.Now);
            // Close the previous observation at reception; browser timestamps never backfill time.
            var before = browser.Enrich(native);
            state ??= Tracker.Start(before, settings);
            var transition = Tracker.Advance(state, before, settings, Command.Stop);
            store.Apply(transition);
            state = transition.State;
            var accepted = browser.Accept(id, message, native, settings, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var activity = browser.Enrich(native).Activity;
            state = state with { Activity = activity, Category = settings.Classify(activity) };
            return accepted;
        }
        finally { gate.Release(); }
    }

    public void Dispose() => gate.Dispose();

    private IHistoryArchive Archive => store as IHistoryArchive ?? throw new NotSupportedException("History archive unavailable.");

    public async Task<StorageInfo> Storage(CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try { return Archive.Inspect(); }
        finally { gate.Release(); }
    }

    public async Task<BackupInfo> ManageHistory(string operation, TimeRange? range, string? id, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            var now = clock.Now;
            if (operation != "backup" && state?.Paused != true)
                throw new ArgumentException("Перед изменением хранилища приостановите учёт.");
            var result = operation switch
            {
                "backup" => Archive.Backup(),
                "restore" => Archive.Restore(id ?? "", now),
                "delete" when range is not null => Archive.Delete(range with { End = Math.Min(range.End, now) }),
                _ => throw new ArgumentException("Неверная операция с историей.")
            };
            if (operation != "backup" && state is not null)
                state = state with { At = now, IdleFloor = now, AutoSince = null, AwaySince = null };
            return result;
        }
        finally { gate.Release(); }
    }

    public async Task<HistoryExport> Export(DateOnly from, DateOnly to, CancellationToken cancellation)
    {
        if (from.Year < 1970 || to.Year > 9998 || to < from || to.DayNumber - from.DayNumber > 365)
            throw new ArgumentException("Экспорт: от 1 до 366 дней, начиная с 1970 года.");
        await gate.WaitAsync(cancellation);
        try
        {
            var days = new List<DayReport>();
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                cancellation.ThrowIfCancellationRequested();
                days.Add(store.ReadDay(day, TimeZoneInfo.Local));
            }
            return new(1, TimeZoneInfo.Local.Id, clock.Now, days);
        }
        finally { gate.Release(); }
    }
}
