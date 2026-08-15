using System.Buffers.Binary;
using System.Security.Cryptography;

namespace WebApi.Hashing;

/// <summary>
/// Fast content sample for large-file dedupe without reading the whole object.
/// Fingerprint = SHA-256( LE64(size) || head[0..N) || tail[size-N..size) ).
/// When size ≤ 2N the entire file is hashed (head covers all; tail is empty).
/// </summary>
public static class ContentFingerprint
{
    public const int DefaultSampleBytes = 1 * 1024 * 1024; // 1 MiB

    public static string ComputeFromSamples(long totalSize, ReadOnlySpan<byte> head, ReadOnlySpan<byte> tail)
    {
        Span<byte> sizeLe = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(sizeLe, totalSize);

        var len = 8 + head.Length + tail.Length;
        var buffer = len <= 256 * 1024
            ? stackalloc byte[len]
            : new byte[len];

        sizeLe.CopyTo(buffer);
        head.CopyTo(buffer[8..]);
        if (tail.Length > 0)
            tail.CopyTo(buffer[(8 + head.Length)..]);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(buffer[..len], hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<string> ComputeFromFileAsync(
        string path,
        long expectedSize,
        int sampleBytes = DefaultSampleBytes,
        CancellationToken ct = default)
    {
        await using var fs = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (fs.Length != expectedSize && expectedSize > 0)
            expectedSize = fs.Length;

        var n = sampleBytes;
        if (expectedSize <= 0)
            return ComputeFromSamples(0, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty);

        if (expectedSize <= n * 2L)
        {
            var all = new byte[expectedSize];
            await fs.ReadExactlyAsync(all, ct);
            return ComputeFromSamples(expectedSize, all, ReadOnlySpan<byte>.Empty);
        }

        var head = new byte[n];
        await fs.ReadExactlyAsync(head, ct);

        fs.Seek(expectedSize - n, SeekOrigin.Begin);
        var tail = new byte[n];
        await fs.ReadExactlyAsync(tail, ct);

        return ComputeFromSamples(expectedSize, head, tail);
    }

    public static string? Normalize(string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return null;

        var normalized = fingerprint.Trim().ToLowerInvariant();
        if (normalized.StartsWith("fp:", StringComparison.Ordinal) ||
            normalized.StartsWith("sample:", StringComparison.Ordinal))
        {
            var idx = normalized.IndexOf(':');
            normalized = normalized[(idx + 1)..];
        }

        if (normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
            throw new ArgumentException(
                "contentFingerprint must be a 64-character hex SHA-256 string",
                nameof(fingerprint));

        return normalized;
    }
}
