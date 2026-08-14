using System.Security.Cryptography;
using WebApi.Interfaces;

namespace WebApi.Hashing;

/// <summary>
/// Default CPU SHA-256 hasher (streaming).
/// To add GPU acceleration later: implement IFileHasher and register it in Program.cs.
/// </summary>
public sealed class Sha256FileHasher : IFileHasher
{
    private const int BufferSize = 1 * 1024 * 1024;

    public async Task<string> ComputeSha256Async(Stream data, CancellationToken ct = default)
    {
        var hash = await SHA256.HashDataAsync(data, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
    {
        await using var fs = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: BufferSize,
            useAsync: true);

        return await ComputeSha256Async(fs, ct);
    }
}
