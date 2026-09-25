using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WorkLifeBalance.Infrastructure;

public static class NativeProtocol
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Identity(string dataDirectory) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Environment.UserName + "|" + Path.GetFullPath(dataDirectory).ToUpperInvariant())))[..24];

    public static async Task<byte[]?> Read(Stream stream, CancellationToken cancellation)
    {
        var header = new byte[4];
        var first = await stream.ReadAsync(header.AsMemory(0, 1), cancellation).ConfigureAwait(false);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1, 3), cancellation).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length is 0 or > 32768) throw new InvalidDataException("Native message exceeds the frame limit.");
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellation).ConfigureAwait(false);
        return payload;
    }
    public static async Task Write(Stream stream, byte[] payload, CancellationToken cancellation)
    {
        if (payload.Length is 0 or > 32768) throw new InvalidDataException("Invalid frame length.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellation).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellation).ConfigureAwait(false);
        await stream.FlushAsync(cancellation).ConfigureAwait(false);
    }
}
