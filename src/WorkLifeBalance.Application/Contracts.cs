using WorkLifeBalance.Domain;

namespace WorkLifeBalance.Application;

public interface IClock { long Now { get; } }
public interface IActivitySource { Observation Observe(long now); }
public interface ITrackerStore : IDisposable
{
    TrackerSettings ReadSettings();
    void WriteSettings(TrackerSettings settings, long? reclassifyUntil = null);
    void Apply(Transition transition);
    DayReport ReadDay(DateOnly date, TimeZoneInfo zone);
    string Correct(IReadOnlyList<TimeRange> ranges, Category category, long now);
    bool Undo(string actionId);
}
public sealed record ProcessEntry(string Executable, int Instances, bool HasWindow, bool Active);
public interface IProcessCatalog { IReadOnlyList<ProcessEntry> List(); }
public interface ILocalLog { void Error(string operation, Exception error); }
public sealed record TrackerSnapshot(TrackerSettings Settings, TrackerState State, DayReport Today,
    string? Error, long ServerTime);
