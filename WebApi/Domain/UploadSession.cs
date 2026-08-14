namespace WebApi.Domain;

public class UploadSession
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Final name on disk (may differ from FileName if conflict resolution is applied).
    /// </summary>
    public string? FinalFileName { get; set; }

    public long TotalSize { get; set; }

    public int ChunkSize { get; set; }

    public int TotalChunks { get; set; }

    public UploadStatus Status { get; set; } = UploadStatus.Pending;

    /// <summary>
    /// Optimistic concurrency token. Incremented on every status/metadata write.
    /// </summary>
    public int Version { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// After this time, a still-Pending session is considered orphan and will be cleaned up.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    public string? Checksum { get; set; }

    public string? ContentType { get; set; }

    /// <summary>
    /// Client IP that initiated the upload (for rate limiting / quota).
    /// </summary>
    public string? ClientIp { get; set; }

    /// <summary>
    /// Comma-separated list of received chunk indexes (legacy/local hint).
    /// Multi-node truth for parts is storage listing, not this CSV.
    /// </summary>
    public string ReceivedChunksCsv { get; set; } = string.Empty;

    public HashSet<int> GetReceivedChunks()
    {
        if (string.IsNullOrWhiteSpace(ReceivedChunksCsv))
            return new HashSet<int>();

        return ReceivedChunksCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse)
            .ToHashSet();
    }

    public void MarkChunkReceived(int index)
    {
        var set = GetReceivedChunks();
        if (set.Add(index))
        {
            ReceivedChunksCsv = string.Join(',', set.OrderBy(x => x));
        }
    }

    public bool IsExpired() =>
        Status is UploadStatus.Pending or UploadStatus.Completing && DateTime.UtcNow >= ExpiresAt;
}
