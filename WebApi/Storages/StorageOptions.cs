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

    public int MaxConcurrentDiskIo { get; set; } = 0;

    public int MergeParallelism { get; set; } = 4;

    public bool SinglePassMergeAndHash { get; set; } = false;

    /// <summary>
    /// When true, require X-Chunk-CRC32 and reject mismatches (deletes bad part).
    /// </summary>
    public bool RequireChunkCrc32 { get; set; } = false;

    /// <summary>
    /// When true, require X-Chunk-SHA256 (64 hex) and reject mismatches (deletes bad part).
    /// Stronger than CRC32; costs one SHA-256 over each chunk body on the server.
    /// </summary>
    public bool RequireChunkSha256 { get; set; } = false;

    public int SessionCacheTtlSeconds { get; set; } = 30;
}
