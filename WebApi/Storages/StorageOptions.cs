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
}
