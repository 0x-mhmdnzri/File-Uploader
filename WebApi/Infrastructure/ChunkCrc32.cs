using System.IO.Hashing;
using System.Buffers;

namespace WebApi.Infrastructure;

public static class ChunkCrc32
{
    /// <summary>
    /// Computes CRC-32 (Castagnoli-free IEEE) over the stream and rewinds is not possible;
    /// caller must pass a stream that will also be consumed for storage, or use tee.
    /// Returns unsigned hex (8 chars lowercase).
    /// </summary>
    public static async Task<string> ComputeHexAsync(Stream data, CancellationToken ct = default)
    {
        var crc = new Crc32();
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            int read;
            while ((read = await data.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                crc.Append(buffer.AsSpan(0, read));
            }

            var hash = crc.GetCurrentHash();
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static string ComputeHex(ReadOnlySpan<byte> data)
    {
        var crc = new Crc32();
        crc.Append(data);
        return Convert.ToHexString(crc.GetCurrentHash()).ToLowerInvariant();
    }

    public static bool EqualsHex(string? expected, string actual)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return false;

        var e = expected.Trim().ToLowerInvariant();
        if (e.StartsWith("0x"))
            e = e[2..];

        return string.Equals(e, actual, StringComparison.OrdinalIgnoreCase);
    }
}
