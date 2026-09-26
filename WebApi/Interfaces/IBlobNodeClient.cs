namespace WebApi.Interfaces;

/// <summary>
/// Client for first-party owned blob nodes (perf 4.1 / D8).
/// When configured, API nodes stay mostly stateless and delegate durable bytes
/// to blob nodes with local SSD. See docs/OWNED-BLOB-NODES.md.
/// </summary>
public interface IBlobNodeClient
{
    string NodeId { get; }

    Task PutObjectAsync(string key, Stream data, long? contentLength = null, CancellationToken ct = default);

    Task<Stream> GetObjectAsync(string key, CancellationToken ct = default);

    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    Task DeleteObjectAsync(string key, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken ct = default);

    /// <summary>
    /// Server-side compose on the blob node (preferred merge path).
    /// Returns false if the node does not support compose (caller falls back).
    /// </summary>
    Task<bool> TryComposeAsync(IReadOnlyList<string> partKeys, string finalKey, CancellationToken ct = default);

    Task<BlobNodeHealth> GetHealthAsync(CancellationToken ct = default);
}

public sealed class BlobNodeHealth
{
    public bool Ready { get; init; }
    public long FreeBytes { get; init; }
    public string? Detail { get; init; }
}
