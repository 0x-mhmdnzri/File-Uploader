using Microsoft.Extensions.Options;
using WebApi.Storages;

namespace WebApi.Infrastructure;

/// <summary>
/// P4.0 non-goals as startup policy. Fails fast when multi-instance is claimed
/// without the architectural prerequisites.
/// </summary>
public static class MultiInstanceStartupGuard
{
    public static void ValidateOrThrow(
        MultiInstanceOptions mi,
        StorageOptions storage,
        string databaseProvider,
        ILogger logger)
    {
        // Always log the non-goals once so operators see them in boot logs.
        logger.LogInformation(
            "P4.0 non-goals: " +
            "(NG1) in-process Semaphore/Mutex/ConcurrentDictionary do not coordinate across machines; " +
            "(NG2) sticky load-balancer is not HA; " +
            "(NG3) external S3/MinIO is not the product storage plane — own shared part store is.");

        if (!mi.Enabled)
        {
            logger.LogInformation("MultiInstance:Enabled=false (single-node lab mode)");
            return;
        }

        logger.LogWarning("MultiInstance:Enabled=true — enforcing shared metadata + shared part-store policy");

        if (mi.RequirePostgres &&
            !string.Equals(databaseProvider, "Postgres", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(databaseProvider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "MultiInstance requires Database:Provider=Postgres (shared metadata). " +
                "SQLite + in-memory cache cannot coordinate multiple API nodes (NG1).");
        }

        if (!mi.SharedPartStoreConfigured)
        {
            throw new InvalidOperationException(
                "MultiInstance requires MultiInstance:SharedPartStoreConfigured=true. " +
                "You must mount the same TempPath/FinalPath (or equivalent) on every API node. " +
                "Sticky LB is not an acceptable substitute (NG2).");
        }

        if (mi.ForbidExternalObjectStoreAsProductPlane &&
            string.Equals(storage.Provider, "S3", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "MultiInstance forbids StorageOptions:Provider=S3 as the product data plane (NG3). " +
                "Use FileSystem on a shared volume (or a future owned blob plane). " +
                "External S3 adapter is experimental only — set ForbidExternalObjectStoreAsProductPlane=false to override for lab.");
        }

        logger.LogInformation(
            "MultiInstance policy OK: Postgres metadata, SharedPartStoreConfigured=true, Provider={Provider}",
            storage.Provider);
    }
}
