namespace WebApi.Storages;

public class StorageOptions
{
    public const string SectionName = "StorageOptions";

    public string Provider { get; set; } = "FileSystem";

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

    public long MaxTotalStoredBytes { get; set; } = 200L * 1024 * 1024 * 1024;
    public long MaxStoredBytesPerIp { get; set; } = 50L * 1024 * 1024 * 1024;

    public int MaxConcurrentDiskIo { get; set; } = 8;

    public int MergeParallelism { get; set; } = 4;

    public bool SinglePassMergeAndHash { get; set; } = true;

    /// <summary>
    /// When true (default), complete always computes full-file SHA-256.
    /// </summary>
    public bool AlwaysComputeFullChecksum { get; set; } = true;

    /// <summary>
    /// When true (default) and client sends SHA-256 at initiate, return existing Completed
    /// session if checksum + size match and the final object still exists on the shared store.
    /// Also enables sample-fingerprint dedupe for large files.
    /// </summary>
    public bool DeduplicateByContent { get; set; } = true;

    /// <summary>
    /// Bytes sampled from head and tail for content fingerprint (default 1 MiB each).
    /// Enables dedupe for multi-GB files without a full client SHA-256.
    /// </summary>
    public int ContentSampleBytes { get; set; } = 1 * 1024 * 1024;

    public bool RequireChunkCrc32 { get; set; } = false;
    public bool RequireChunkSha256 { get; set; } = false;

    public int SessionCacheTtlSeconds { get; set; } = 30;

    public string Hasher { get; set; } = "Hardware";
}
