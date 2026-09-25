using WorkLifeBalance.Infrastructure;
using Xunit;

namespace WorkLifeBalance.Tests;

public sealed class NativeProtocolTests
{
    [Fact]
    public async Task Framing_preserves_payload_and_handles_eof()
    {
        using var stream = new MemoryStream();
        await NativeProtocol.Write(stream, "{\"version\":1}"u8.ToArray(), CancellationToken.None);
        stream.Position = 0;
        Assert.Equal("{\"version\":1}"u8.ToArray(), await NativeProtocol.Read(stream, CancellationToken.None));
        Assert.Null(await NativeProtocol.Read(stream, CancellationToken.None));
    }
    [Fact]
    public async Task Oversized_or_truncated_frames_are_rejected()
    {
        using var oversized = new MemoryStream([255, 255, 255, 127]);
        await Assert.ThrowsAsync<InvalidDataException>(() => NativeProtocol.Read(oversized, CancellationToken.None));
        using var truncated = new MemoryStream([5, 0, 0, 0, 1]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => NativeProtocol.Read(truncated, CancellationToken.None));
    }
}
