namespace WebApi.Infrastructure;

/// <summary>
/// P4.0 — operational switch for multi-instance deployments.
/// Non-goals are enforced here so sticky LB / in-process locks / external S3
/// are not mistaken for the product architecture.
/// </summary>
public class MultiInstanceOptions
{
    public const string SectionName = "MultiInstance";

    /// <summary>
    /// When true, startup validates shared-metadata and shared-part-store policy.
    /// Leave false for single-node lab.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Ops must set true only when TempPath/FinalPath are on a volume visible to every API node.
    /// Code cannot prove NFS topology; this is an explicit acknowledgment (D6 gate).
    /// </summary>
    public bool SharedPartStoreConfigured { get; set; }

    /// <summary>
    /// When true and MultiInstance is enabled, refuse to start if Database:Provider is not Postgres.
    /// Default true — SQLite is not a multi-node metadata plane.
    /// </summary>
    public bool RequirePostgres { get; set; } = true;

    /// <summary>
    /// When true, refuse Provider=S3 as the product data plane under MultiInstance.
    /// External S3/MinIO adapter remains for experiments only (NG3).
    /// </summary>
    public bool ForbidExternalObjectStoreAsProductPlane { get; set; } = true;
}
