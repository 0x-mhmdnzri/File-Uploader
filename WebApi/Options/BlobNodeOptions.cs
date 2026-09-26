namespace WebApi.Options;

/// <summary>
/// Configuration for owned blob nodes (perf 4.1). Empty Nodes = feature off;
/// product plane remains shared filesystem / lab S3.
/// </summary>
public class BlobNodeOptions
{
    public const string SectionName = "BlobNodes";

    /// <summary>
    /// When false (default), IFileStorage stays FileSystem/S3 as today.
    /// When true, BlobNode-backed storage is used and <see cref="Nodes"/> must be non-empty.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Placement: HashUploadId | Primary | LeastLoaded
    /// </summary>
    public string Placement { get; set; } = "HashUploadId";

    /// <summary>
    /// Service token for API → blob node auth (optional in lab).
    /// </summary>
    public string? ServiceToken { get; set; }

    public List<BlobNodeEndpoint> Nodes { get; set; } = [];
}

public class BlobNodeEndpoint
{
    public string Id { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    /// <summary>Optional weight for least-loaded / hash ring.</summary>
    public int Weight { get; set; } = 1;
}
