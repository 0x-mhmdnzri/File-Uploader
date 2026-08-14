namespace WebApi.Domain;

public class UploadSession
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string? FinalFileName { get; set; }

    public long TotalSize { get; set; }

    public int ChunkSize { get; set; }

    public int TotalChunks { get; set; }

    public UploadStatus Status { get; set; } = UploadStatus.Pending;

    /// <summary>
    /// Optimistic concurrency token. Incremented on status/metadata writes.
    /// </summary>
    public int Version { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public string? Checksum { get; set; }

    public string? ContentType { get; set; }

    public string? ClientIp { get; set; }

    /// <summary>
    /// Legacy/local hint. Multi-node part truth is storage listing.
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
            ReceivedChunksCsv = string.Join(',', set.OrderBy(x => x));
    }

    public bool IsExpired() =>
        (Status is UploadStatus.Pending or UploadStatus.Completing) && DateTime.UtcNow >= ExpiresAt;
}
