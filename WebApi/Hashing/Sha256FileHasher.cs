using System.Buffers;
using System.Security.Cryptography;
using WebApi.Interfaces;

namespace WebApi.Hashing;

/// <summary>
/// Streaming SHA-256 with ArrayPool buffers and sequential-scan file opens.
/// </summary>
public sealed class Sha256FileHasher : IFileHasher
{
    private const int BufferSize = 4 * 1024 * 1024; // 4 MB — fewer syscalls on large files

    public async Task<string> ComputeSha256Async(Stream data, CancellationToken ct = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var sha = SHA256.Create();
            int read;
            while ((read = await data.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
            {
                sha.TransformBlock(buffer, 0, read, null, 0);
            }

            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
    {
        await using var fs = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: BufferSize,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await ComputeSha256Async(fs, ct).ConfigureAwait(false);
    }
}
