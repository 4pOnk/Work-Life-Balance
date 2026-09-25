using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WorkLifeBalance.Application;
using WorkLifeBalance.Domain;
using Activity = WorkLifeBalance.Domain.Activity;

namespace WorkLifeBalance.Windows;

public sealed class WindowsActivitySource : IActivitySource, IProcessCatalog
{
    private uint lastTick;
    private long lastInput;
    private bool initialized;

    public Observation Observe(long now)
    {
        var info = new Native.LastInputInfo { Size = (uint)Marshal.SizeOf<Native.LastInputInfo>() };
        if (!Native.GetLastInputInfo(ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        // Cache the mapped input timestamp: recomputing it introduces jitter and false AFK exits.
        if (!initialized || info.Tick != lastTick)
        {
            var age = unchecked((uint)Environment.TickCount64 - info.Tick);
            lastInput = now - age;
            lastTick = info.Tick;
            initialized = true;
        }
        var window = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(window, out var pid);
        return new(now, lastInput, Executable(pid), window);
    }

    private static Activity Executable(uint pid)
    {
        try
        {
            if (pid == 0) return Activity.Unknown;
            using var process = Process.GetProcessById((int)pid);
            return new(process.ProcessName + ".exe");
        }
        catch (ArgumentException) { return Activity.Unknown; }
        catch (InvalidOperationException) { return Activity.Unknown; }
        catch (Win32Exception) { return Activity.Unknown; }
    }

    public IReadOnlyList<ProcessEntry> List()
    {
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out var active);
        var entries = new List<ProcessEntry>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { entries.Add(new(process.ProcessName + ".exe", 1, process.MainWindowHandle != 0, process.Id == active)); }
                catch (InvalidOperationException) { /* Process exited during enumeration. */ }
                catch (Win32Exception) { /* Protected process; no elevation required. */ }
            }
        }
        return entries.GroupBy(e => e.Executable, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ProcessEntry(g.Key, g.Count(), g.Any(e => e.HasWindow), g.Any(e => e.Active)))
            .OrderByDescending(e => e.Active).ThenBy(e => e.Executable).ToArray();
    }

    public static bool IsSessionLocked()
    {
        if (!Native.WTSQuerySessionInformation(0, -1, 25, out var buffer, out var bytes))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            // WTSINFOEX: DWORD Level, aligned union with SessionId, SessionState, SessionFlags.
            return bytes >= 20 && Marshal.ReadInt32(buffer) == 1 && Marshal.ReadInt32(buffer, 16) == 0;
        }
        finally { Native.WTSFreeMemory(buffer); }
    }
}
