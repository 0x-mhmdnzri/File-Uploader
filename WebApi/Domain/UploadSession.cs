using System.Collections.Concurrent;

namespace WebApi.Domain;

public class UploadSession
{
    public Guid Id { get; set; }
    public string FileName { get; set; }
    public long TotalSize { get; set; }
    public int ChunkSize { get; set; }
    public int TotalChunks { get; set; }
    public ConcurrentDictionary<int,bool> ReceivedChunks { get; set; } = new ConcurrentDictionary<int,bool>();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool Completed { get; set; }
    public string TempFolder { get; set; }
}