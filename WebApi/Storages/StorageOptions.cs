namespace WebApi.Storages;

public class StorageOptions
{
    public const string SectionName = "StorageOptions";

    public string TempPath { get; set; } = "temp";
    public string FinalPath { get; set; } = "uploads";

    /// <summary>
    /// How long a Pending upload remains valid before being cleaned up (hours).
    /// </summary>
    public int PendingTtlHours { get; set; } = 24;

    /// <summary>
    /// How often the orphan cleanup job runs (minutes).
    /// </summary>
    public int CleanupIntervalMinutes { get; set; } = 60;

    // ---------- Security limits ----------

    /// <summary>
    /// Maximum allowed total file size in bytes. Default: 20 GB.
    /// Set to 0 or negative to disable.
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 20L * 1024 * 1024 * 1024;

    /// <summary>
    /// Maximum allowed chunk size in bytes. Default: 32 MB.
    /// </summary>
    public int MaxChunkSizeBytes { get; set; } = 32 * 1024 * 1024;

    /// <summary>
    /// Allowed file extensions (without dot), e.g. ["pdf", "png", "jpg"].
    /// If empty, all extensions are allowed (unless blocked).
    /// Comparison is case-insensitive.
    /// </summary>
    public string[] AllowedExtensions { get; set; } = [];

    /// <summary>
    /// Blocked file extensions (without dot), e.g. ["exe", "bat", "cmd", "ps1"].
    /// Always applied, even if AllowedExtensions is empty.
    /// </summary>
    public string[] BlockedExtensions { get; set; } =
    [
        "exe", "bat", "cmd", "com", "msi", "scr", "ps1", "vbs", "js", "jar", "dll", "sh"
    ];

    /// <summary>
    /// Max number of concurrent Pending sessions per client IP.
    /// Set to 0 or negative to disable.
    /// </summary>
    public int MaxPendingSessionsPerIp { get; set; } = 5;

    /// <summary>
    /// Global concurrent disk IO gate (SaveChunk + parallel merge workers).
    /// Prevents FS thrashing under heavy parallel uploads.
    /// Default: Environment.ProcessorCount (clamped 2–16).
    /// </summary>
    public int MaxConcurrentDiskIo { get; set; } = 0;

    /// <summary>
    /// Degree of parallelism for offset-based merge. Default 4.
    /// </summary>
    public int MergeParallelism { get; set; } = 4;
}
