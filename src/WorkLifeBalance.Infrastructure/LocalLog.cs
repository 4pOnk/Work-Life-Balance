using WorkLifeBalance.Application;

namespace WorkLifeBalance.Infrastructure;

public sealed class LocalLog(string directory) : ILocalLog
{
    private readonly object sync = new();
    public void Error(string operation, Exception error)
    {
        lock (sync)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "errors.log");
                if (File.Exists(path) && new FileInfo(path).Length > 512_000)
                    File.Move(path, path + ".1", true);
                // Exception messages may contain file paths, URLs or tokens. Keep only type/code.
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {operation} {error.GetType().Name} 0x{error.HResult:X8}{Environment.NewLine}");
            }
            catch (IOException) { System.Diagnostics.Trace.WriteLine("Local diagnostic log is unavailable."); }
            catch (UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine("Local diagnostic log access denied."); }
        }
    }
}
