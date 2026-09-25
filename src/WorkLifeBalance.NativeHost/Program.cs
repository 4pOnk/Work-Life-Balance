using System.IO.Pipes;
using System.Text.Json;
using WorkLifeBalance.Infrastructure;

var dataIndex = Array.IndexOf(args, "--data-dir");
var directory = dataIndex >= 0 && dataIndex + 1 < args.Length ? Path.GetFullPath(args[dataIndex + 1]) :
    Environment.GetEnvironmentVariable("WLB_NATIVE_DATA_DIR") ??
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkLifeBalance");
var identity = NativeProtocol.Identity(directory);
using var input = Console.OpenStandardInput();
using var output = Console.OpenStandardOutput();
NamedPipeClientStream? pipe = null;
try
{
    while (await NativeProtocol.Read(input, CancellationToken.None) is { } bytes)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        byte[] response;
        try
        {
            if (pipe is null)
            {
                pipe = new NamedPipeClientStream(".", "WorkLifeBalance-browser-" + identity,
                    PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(timeout.Token);
            }
            await NativeProtocol.Write(pipe, bytes, timeout.Token);
            response = await NativeProtocol.Read(pipe, timeout.Token) ?? throw new EndOfStreamException();
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            pipe?.Dispose(); pipe = null;
            response = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, ok = false, status = "tracker-offline" });
        }
        await NativeProtocol.Write(output, response, CancellationToken.None);
    }
}
catch (IOException) { /* Firefox closed stdin/stdout, or rejected an invalid frame. */ }
finally { pipe?.Dispose(); }
