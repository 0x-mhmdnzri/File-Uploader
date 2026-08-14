namespace WebApi.Storages;

public class StorageOptions
{
    public const string SectionName = "StorageOptions";

    public string TempPath { get; set; } = "temp";
    public string FinalPath { get; set; } = "uploads";

    public int PendingTtlHours { get; set; } = 24;
    public int CleanupIntervalMinutes { get; set; } = 60;

    public long MaxFileSizeBytes { get; set; } = 20L * 1024 * 1024 * 1024;
    public int MaxChunkSizeBytes { get; set; } = 32 * 1024 * 1024;

    public string[] AllowedExtensions { get; set; } = [];

    public string[] BlockedExtensions { get; set; } =
    [
        "exe", "bat", "cmd", "com", "msi", "scr", "ps1", "vbs", "js", "jar", "dll", "sh"
    ];

    public int MaxPendingSessionsPerIp { get; set; } = 5;

    /// <summary>
    /// Global concurrent disk IO gate. 0 = clamp(ProcessorCount, 2, 16).
    /// </summary>
    public int MaxConcurrentDiskIo { get; set; } = 0;

    /// <summary>
    /// Degree of parallelism for offset-based merge.
    /// </summary>
    public int MergeParallelism { get; set; } = 4;

    /// <summary>
    /// When true, merge copies parts in order while updating SHA-256 (true single pass).
    /// When false (default), parallel offset writes then sequential hash.
    /// </summary>
    public bool SinglePassMergeAndHash { get; set; } = false;

    /// <summary>
    /// When true, require X-Chunk-CRC32 header and reject mismatched chunks.
    /// </summary>
    public bool RequireChunkCrc32 { get; set; } = false;

    /// <summary>
    /// Session cache TTL in seconds for hot-path chunk validation.
    /// </summary>
    public int SessionCacheTtlSeconds { get; set; } = 30;
}
