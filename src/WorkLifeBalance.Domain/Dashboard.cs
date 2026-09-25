namespace WorkLifeBalance.Domain;

public sealed record Totals(double WorkSeconds, double RestSeconds, double AfkSeconds, double? WorkShare);
public sealed record HourReport(long Start, string Label, double WorkSeconds, double RestSeconds, double AfkSeconds);
public sealed record AppReport(string Executable, double WorkSeconds, double RestSeconds, double AfkSeconds);
public sealed record DayDetail(DayReport Report, IReadOnlyList<HourReport> Hours, IReadOnlyList<AppReport> Apps,
    double PauseSeconds, double UnknownSeconds, double FutureSeconds, double ProvisionalSeconds);
public sealed record CalendarDay(string Date, string State, int Level, Totals Totals,
    int TaskCount = 0, int TaskWeight = 0, int TaskLevel = 0, bool HasActivity = false);
public sealed record WeekReport(string Start, string End, Totals Totals);
public sealed record MonthReport(string Month, string TimeZone, IReadOnlyList<CalendarDay> Days,
    Totals Totals, IReadOnlyList<WeekReport> Weeks);
public sealed record DashboardReport(DayDetail Day, MonthReport Month, TodoDay? Todos = null);

public static class Dashboard
{
    public static Totals Sum(IEnumerable<DayReport> days)
    {
        var list = days.ToArray();
        var work = list.Sum(x => x.WorkSeconds);
        var rest = list.Sum(x => x.RestSeconds);
        return new(work, rest, list.Sum(x => x.AfkSeconds), work + rest > 0 ? work / (work + rest) : null);
    }
    // Fixed absolute scale, identical for every month.
    public static int Level(double workSeconds) => workSeconds <= 0 ? 0 : workSeconds < 3600 ? 1 :
        workSeconds < 14400 ? 2 : workSeconds < 28800 ? 3 : 4;
    public static int TaskLevel(int weight) => weight <= 0 ? 0 : weight < 5 ? 1 : weight < 10 ? 2 : weight < 15 ? 3 : 4;

    public static DayDetail Detail(DayReport day, TimeZoneInfo zone, long now, long? provisionalSince = null)
    {
        static double Overlap(long start, long end, long left, long right) => Math.Max(0, Math.Min(end, right) - Math.Max(start, left)) / 1000d;
        var hours = new List<HourReport>();
        // UTC buckets preserve both occurrences of repeated local hours on DST fall-back.
        for (var start = day.Start; start < day.End; start += 3600000)
        {
            var end = Math.Min(start + 3600000, day.End);
            double Total(Category category) => day.Intervals.Where(x => x.Category == category).Sum(x => Overlap(x.Start, x.End, start, end));
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(start), zone);
            hours.Add(new(start, local.ToString("HH:mm zzz"), Total(Category.Work), Total(Category.Rest), Total(Category.Afk)));
        }
        var apps = day.Intervals.GroupBy(x => x.Executable, StringComparer.OrdinalIgnoreCase).Select(group =>
        {
            double Total(Category category) => group.Where(x => x.Category == category).Sum(x => (x.End - x.Start) / 1000d);
            return new AppReport(group.Key, Total(Category.Work), Total(Category.Rest), Total(Category.Afk));
        }).OrderByDescending(x => x.WorkSeconds + x.RestSeconds + x.AfkSeconds).ThenBy(x => x.Executable).ToArray();
        var elapsedEnd = Math.Clamp(now, day.Start, day.End);
        var paused = (day.Pauses ?? []).Sum(x => Overlap(x.Start, x.End, day.Start, elapsedEnd));
        var tracked = day.Intervals.Sum(x => Overlap(x.Start, x.End, day.Start, elapsedEnd));
        var provisional = provisionalSince is { } since ? day.Intervals.Where(x => x.Category != Category.Afk && x.CorrectionId is null)
            .Sum(x => Overlap(x.Start, x.End, since, now)) : 0;
        return new(day, hours, apps, paused, Math.Max(0, (elapsedEnd - day.Start) / 1000d - tracked - paused),
            (day.End - elapsedEnd) / 1000d, provisional);
    }

    public static MonthReport Month(DateOnly month, TimeZoneInfo zone, IReadOnlyList<DayReport> reports, long now)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(now), zone).DateTime);
        var days = reports.Select(day =>
        {
            var date = DateOnly.ParseExact(day.Date, "yyyy-MM-dd");
            var count = day.CompletedTodos?.Count ?? 0;
            var weight = day.CompletedTodos?.Sum(x => x.Weight) ?? 0;
            var activity = day.Intervals.Count > 0 || (day.Pauses?.Count ?? 0) > 0;
            var state = date > today ? "future" : !activity && count == 0 ? "empty" : "recorded";
            return new CalendarDay(day.Date, state, Level(day.WorkSeconds), Sum([day]), count, weight, TaskLevel(weight), activity);
        }).ToArray();
        var weeks = reports.GroupBy(day =>
        {
            var date = DateOnly.ParseExact(day.Date, "yyyy-MM-dd");
            return date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        }).Select(group => new WeekReport(group.First().Date, group.Last().Date, Sum(group))).ToArray();
        return new(month.ToString("yyyy-MM"), zone.Id, days, Sum(reports), weeks);
    }
}
