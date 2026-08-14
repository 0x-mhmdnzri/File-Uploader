using System.Buffers;
using System.Security.Cryptography;
using WebApi.Interfaces;

namespace WebApi.Hashing;

/// <summary>
/// SHA-256 using platform crypto (often hardware-accelerated via OS CNG/OpenSSL).
/// Register instead of Sha256FileHasher when you want explicit "prefer hardware" semantics.
/// True discrete-GPU hashing requires a vendor library; plug it in behind IFileHasher.
/// </summary>
public sealed class HardwareSha256FileHasher : IFileHasher
{
    private const int BufferSize = 4 * 1024 * 1024;

    public async Task<string> ComputeSha256Async(Stream data, CancellationToken ct = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int read;
            while ((read = await data.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
            {
                sha.AppendData(buffer.AsSpan(0, read));
            }

            return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
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
