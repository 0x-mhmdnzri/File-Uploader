using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using WebApi.Storages;

namespace WebApi.Health;

/// <summary>
/// Verifies that temp and final storage directories exist and are writable.
/// </summary>
public class StorageHealthCheck : IHealthCheck
{
    private readonly StorageOptions _options;

    public StorageHealthCheck(IOptions<StorageOptions> options)
    {
        _options = options.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var temp = Path.GetFullPath(_options.TempPath);
            var final = Path.GetFullPath(_options.FinalPath);

            Directory.CreateDirectory(temp);
            Directory.CreateDirectory(final);

            // Write probe files
            ProbeWrite(temp);
            ProbeWrite(final);

            long? freeBytes = null;
            try
            {
                var root = Path.GetPathRoot(final);
                if (!string.IsNullOrEmpty(root))
                {
                    var di = new DriveInfo(root);
                    freeBytes = di.AvailableFreeSpace;
                }
            }
            catch
            {
                // non-critical
            }

            var data = new Dictionary<string, object>
            {
                ["tempPath"] = temp,
                ["finalPath"] = final
            };
            if (freeBytes.HasValue)
                data["availableFreeBytes"] = freeBytes.Value;

            return Task.FromResult(HealthCheckResult.Healthy("Storage directories are writable", data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Storage check failed", ex));
        }
    }

    private static void ProbeWrite(string dir)
    {
        var probe = Path.Combine(dir, ".health_probe");
        File.WriteAllText(probe, DateTime.UtcNow.ToString("O"));
        File.Delete(probe);
    }
}
