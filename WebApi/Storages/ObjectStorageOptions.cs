namespace WebApi.Storages;

/// <summary>
/// S3-compatible object storage (AWS S3, MinIO, R2, etc.).
/// Used when StorageOptions.Provider = "S3".
/// </summary>
public class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    /// <summary>Service URL for MinIO/R2 (e.g. http://127.0.0.1:9000). Empty = AWS default.</summary>
    public string? ServiceUrl { get; set; }

    public string Region { get; set; } = "us-east-1";
    public string Bucket { get; set; } = "uploads";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";

    /// <summary>Prefix for temp parts.</summary>
    public string TempPrefix { get; set; } = "temp/";

    /// <summary>Prefix for final objects.</summary>
    public string FinalPrefix { get; set; } = "files/";

    /// <summary>Force path-style (required for most MinIO setups).</summary>
    public bool ForcePathStyle { get; set; } = true;
}
