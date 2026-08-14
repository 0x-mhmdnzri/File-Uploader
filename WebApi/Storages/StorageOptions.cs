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
    /// Global max stored bytes (Completed + active Pending reserved). 0 = unlimited.
    /// Default 200 GB.
    /// </summary>
    public long MaxTotalStoredBytes { get; set; } = 200L * 1024 * 1024 * 1024;

    /// <summary>
    /// Per-IP max stored bytes (Completed + active Pending). 0 = unlimited.
    /// Default 50 GB.
    /// </summary>
    public long MaxStoredBytesPerIp { get; set; } = 50L * 1024 * 1024 * 1024;

    public int MaxConcurrentDiskIo { get; set; } = 0;

    public int MergeParallelism { get; set; } = 4;

    public bool SinglePassMergeAndHash { get; set; } = false;

    public bool RequireChunkCrc32 { get; set; } = false;

    public bool RequireChunkSha256 { get; set; } = false;

    public int SessionCacheTtlSeconds { get; set; } = 30;
}
