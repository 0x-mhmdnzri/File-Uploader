namespace WebApi.Domain;

public enum UploadStatus
{
    Pending,
    Completed,
    Expired,
    Failed
}

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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// After this time, a still-Pending session is considered orphan and will be cleaned up.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    public string? Checksum { get; set; }

    public string? ContentType { get; set; }

    /// <summary>
    /// Client IP that initiated the upload (for rate limiting).
    /// </summary>
    public string? ClientIp { get; set; }

    /// <summary>
    /// Comma-separated list of received chunk indexes (e.g. "0,1,2,5").
    /// Kept simple for SQLite; can be moved to a child table later if needed.
    /// </summary>
    public string ReceivedChunksCsv { get; set; } = string.Empty;

    // ---------- helpers (not mapped) ----------

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

    public bool IsExpired() => Status == UploadStatus.Pending && DateTime.UtcNow >= ExpiresAt;
}
