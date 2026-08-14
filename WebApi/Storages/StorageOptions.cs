namespace WebApi.Storages;

public class StorageOptions
{
    public const string SectionName = "StorageOptions";

    /// <summary>
    /// "FileSystem" (default) or "S3" (AWS/MinIO/R2 via ObjectStorage section).
    /// </summary>
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

    /// <summary>
    /// Global concurrent disk/object IO gate. 0 = clamp(ProcessorCount, 2, 16).
    /// </summary>
    public int MaxConcurrentDiskIo { get; set; } = 8;

    /// <summary>
    /// Parallelism for offset merge / parallel verify.
    /// </summary>
    public int MergeParallelism { get; set; } = 4;

    /// <summary>
    /// true = ordered single-pass merge+SHA (prefer when complete() hash dominates).
    /// false = parallel offset assemble then hash (prefer on fast SSD assemble-bound).
    /// </summary>
    public bool SinglePassMergeAndHash { get; set; } = false;

    public bool RequireChunkCrc32 { get; set; } = false;
    public bool RequireChunkSha256 { get; set; } = false;

    public int SessionCacheTtlSeconds { get; set; } = 30;

    /// <summary>
    /// "Cpu" (default Sha256FileHasher) or "Hardware" (IncrementalHash / OS crypto).
    /// </summary>
    public string Hasher { get; set; } = "Hardware";
}
