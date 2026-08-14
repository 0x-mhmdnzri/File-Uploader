using Microsoft.Extensions.Options;
using WebApi.Domain;
using WebApi.Interfaces;
using WebApi.Storages;

namespace WebApi.BackgroundServices;

/// <summary>
/// Periodically removes expired Pending uploads (orphans) from both database and disk.
/// </summary>
public class OrphanCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StorageOptions _options;
    private readonly ILogger<OrphanCleanupService> _logger;

    public OrphanCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<StorageOptions> options,
        ILogger<OrphanCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(5, _options.CleanupIntervalMinutes));

        _logger.LogInformation(
            "OrphanCleanupService started. Interval={Interval}, PendingTtlHours={Ttl}",
            interval, _options.PendingTtlHours);

        // Small delay on startup so the app can finish booting
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during orphan cleanup cycle");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task CleanupOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUploadRepository>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        var expired = await repo.GetExpiredPendingAsync(ct);

        if (expired.Count == 0)
        {
            _logger.LogDebug("Orphan cleanup: no expired sessions found");
            return;
        }

        _logger.LogInformation("Orphan cleanup: found {Count} expired pending sessions", expired.Count);

        foreach (var session in expired)
        {
            try
            {
                await storage.DeleteTempFolderAsync(session.Id, ct);

                session.Status = UploadStatus.Expired;
                await repo.UpdateAsync(session, ct);

                // Optionally hard-delete the record after marking Expired
                // await repo.DeleteAsync(session, ct);

                _logger.LogInformation(
                    "Cleaned orphan upload {UploadId} ({FileName})", session.Id, session.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to clean orphan upload {UploadId}", session.Id);
            }
        }
    }
}
