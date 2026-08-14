namespace WebApi.Interfaces;

/// <summary>
/// Port for content hashing. Default: CPU SHA-256.
/// GPU-accelerated implementations can replace this without touching upload flow.
/// </summary>
public interface IFileHasher
{
    /// <summary>Returns lowercase hex digest.</summary>
    Task<string> ComputeSha256Async(Stream data, CancellationToken ct = default);

    /// <summary>Hash a file path (streaming).</summary>
    Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default);
}
