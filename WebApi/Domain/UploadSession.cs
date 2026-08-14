using System.Collections.Concurrent;

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
    public long TotalSize { get; set; }
    public int ChunkSize { get; set; }
    public int TotalChunks { get; set; }
    public ConcurrentDictionary<int, bool> ReceivedChunks { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public bool Completed { get; set; }
    public UploadStatus Status { get; set; } = UploadStatus.Pending;
    public string? ClientIp { get; set; }
    public string TempFolder { get; set; } = string.Empty;
}
