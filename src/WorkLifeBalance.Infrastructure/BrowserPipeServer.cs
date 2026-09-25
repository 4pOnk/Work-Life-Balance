using System.IO.Pipes;
using System.Text.Json;
using WorkLifeBalance.Application;

namespace WorkLifeBalance.Infrastructure;

public sealed class BrowserPipeServer(Coordinator coordinator, string identity, int port, ILocalLog log)
{
    public async Task Run(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            var id = Guid.NewGuid();
            try
            {
                await using var pipe = new NamedPipeServerStream("WorkLifeBalance-browser-" + identity, PipeDirection.InOut,
                    1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation).ConfigureAwait(false);
                await coordinator.BrowserConnection(id, true, cancellation).ConfigureAwait(false);
                while (pipe.IsConnected && !cancellation.IsCancellationRequested)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    var bytes = await NativeProtocol.Read(pipe, timeout.Token).ConfigureAwait(false);
                    if (bytes is null) break;
                    var message = JsonSerializer.Deserialize<BrowserMessage>(bytes, NativeProtocol.Json);
                    var accepted = message is not null && await coordinator.BrowserObservation(id, message, timeout.Token).ConfigureAwait(false);
                    var settings = await coordinator.GetSettings(timeout.Token).ConfigureAwait(false);
                    await NativeProtocol.Write(pipe, JsonSerializer.SerializeToUtf8Bytes(new
                    { version = 1, ok = accepted, status = accepted ? "connected" : "rejected", port, settings.BrowserPath }, NativeProtocol.Json), timeout.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* Shutdown or expired browser heartbeat. */ }
            catch (IOException) { /* Browser/bridge closed or sent an invalid frame. */ }
            catch (JsonException) { /* Invalid messages cannot mutate state. */ }
            catch (Exception e) { log.Error("browser-pipe", e); }
            finally { await coordinator.BrowserConnection(id, false).ConfigureAwait(false); }
        }
    }
}
