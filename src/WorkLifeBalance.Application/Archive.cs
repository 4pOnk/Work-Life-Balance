using System.Globalization;
using System.Text;
using WorkLifeBalance.Domain;

namespace WorkLifeBalance.Application;

public sealed record BackupInfo(string Id, long Created, long Bytes);
public sealed record StorageInfo(long DatabaseBytes, long? FirstRecorded, IReadOnlyList<BackupInfo> Backups);
public interface IHistoryArchive
{
    StorageInfo Inspect();
    BackupInfo Backup();
    BackupInfo Restore(string id, long now);
    BackupInfo Delete(TimeRange range);
}

public sealed record HistoryExport(int Version, string TimeZone, long Created, IReadOnlyList<DayReport> Days);
public static class ExportFormat
{
    public static string Csv(HistoryExport export)
    {
        var text = new StringBuilder("date,start_utc,end_utc,seconds,category,executable,url,domain,title,source,task_weight,task_id\r\n");
        foreach (var day in export.Days)
        {
            foreach (var slice in day.Intervals)
                Row(day.Date, slice.Start, slice.End, slice.Category.ToString(), slice.Executable,
                    slice.Browser?.Url, slice.Browser?.Domain, slice.Browser?.Title, slice.Source, null, null);
            foreach (var pause in day.Pauses ?? [])
                Row(day.Date, pause.Start, pause.End, "Pause", null, null, null, null, "pause", null, null);
            foreach (var todo in day.CompletedTodos ?? [])
                Row(day.Date, todo.CompletedAt!.Value, todo.CompletedAt.Value, "Todo", null, null, null,
                    todo.Title, "todo", todo.Weight.ToString(CultureInfo.InvariantCulture), todo.Id);
        }
        return text.ToString();

        void Row(string date, long start, long end, params string?[] values)
        {
            text.Append(string.Join(',', new[] { date, DateTimeOffset.FromUnixTimeMilliseconds(start).ToString("O"),
                DateTimeOffset.FromUnixTimeMilliseconds(end).ToString("O"), ((end-start)/1000d).ToString(CultureInfo.InvariantCulture) }
                .Concat(values).Select(Cell))).Append("\r\n");
        }
    }

    private static string Cell(string? value)
    {
        value ??= "";
        var trimmed = value.TrimStart();
        if (value.Any(c => c is '\t' or '\r' or '\n') || (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]))) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
