namespace WorkLifeBalance.Domain;

public sealed record DayReport(string Date, string TimeZone, double WorkSeconds, double RestSeconds,
    double AfkSeconds, double? WorkShare, IReadOnlyList<Slice> Intervals, long Start = 0, long End = 0,
    IReadOnlyList<TimeRange>? Pauses = null, IReadOnlyList<TodoItem>? CompletedTodos = null);

public static class Reports
{
    public static (long Start, long End) Bounds(DateOnly date, TimeZoneInfo zone)
    {
        static DateTime ToUtc(DateOnly day, TimeZoneInfo zone)
        {
            var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
            return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }
        return (new DateTimeOffset(ToUtc(date, zone)).ToUnixTimeMilliseconds(),
            new DateTimeOffset(ToUtc(date.AddDays(1), zone)).ToUnixTimeMilliseconds());
    }

    public static DayReport Day(DateOnly date, TimeZoneInfo zone, IReadOnlyList<Slice> raw, IReadOnlyList<AwayRange> away,
        IReadOnlyList<Correction>? corrections = null, IReadOnlyList<RuleRevision>? revisions = null)
    {
        var (start, end) = Bounds(date, zone);
        var result = new List<Slice>();
        corrections ??= [];
        revisions ??= [];
        foreach (var slice in raw)
        {
            var left = Math.Max(start, slice.Start);
            var right = Math.Min(end, slice.End);
            if (left >= right) continue;
            var overlaps = away.Where(a => a.Start < right && a.End > left).ToArray();
            var edits = corrections.Where(a => a.Start < right && a.End > left).ToArray();
            var boundaries = overlaps.SelectMany(a => new[] { Math.Max(a.Start, left), Math.Min(a.End, right) })
                .Concat(edits.SelectMany(a => new[] { Math.Max(a.Start, left), Math.Min(a.End, right) }))
                .Concat(revisions.Where(r => r.Until > left && r.Until < right).Select(r => r.Until))
                .Append(left).Append(right).Distinct().Order().ToArray();
            for (var i = 0; i + 1 < boundaries.Length; i++)
            {
                var edit = edits.Where(e => e.Start <= boundaries[i] && e.End > boundaries[i]).MaxBy(e => e.Id);
                var absence = overlaps.FirstOrDefault(a => a.Start <= boundaries[i] && a.End > boundaries[i]);
                var revision = revisions.Where(r => r.Until > boundaries[i]).MaxBy(r => r.Id);
                var browser = slice.Executable.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase);
                // Website rules are captured at observation time; process revisions cannot overwrite them.
                var category = browser ? (slice.Browser is { Private: false, Domain: not null } ? slice.Category : Category.Rest) : revision is null ? slice.Category :
                    revision.WorkProcesses.Contains(slice.Executable, StringComparer.OrdinalIgnoreCase) ? Category.Work : Category.Rest;
                var part = slice with
                {
                    Start = boundaries[i],
                    End = boundaries[i + 1],
                    Category = edit?.Category ?? (absence is not null ? Category.Afk : category),
                    Source = edit is not null ? "manualEdit" : absence is not null ? absence.Reason :
                        browser ? (category == Category.Work ? "siteRule" : "browserDefault") : revision is not null ? "reclassified" : "appRule",
                    CorrectionId = edit?.ActionId
                };
                if (result.Count > 0 && result[^1].End == part.Start && result[^1].Category == part.Category &&
                    result[^1].Executable == part.Executable && result[^1].Browser == part.Browser &&
                    result[^1].Source == part.Source && result[^1].CorrectionId == part.CorrectionId)
                    result[^1] = result[^1] with { End = part.End };
                else result.Add(part);
            }
        }
        double Total(Category category) => result.Where(x => x.Category == category).Sum(x => (x.End - x.Start) / 1000d);
        var work = Total(Category.Work);
        var rest = Total(Category.Rest);
        return new(date.ToString("yyyy-MM-dd"), zone.Id, work, rest, Total(Category.Afk),
            work + rest > 0 ? work / (work + rest) : null, result, start, end);
    }
}
