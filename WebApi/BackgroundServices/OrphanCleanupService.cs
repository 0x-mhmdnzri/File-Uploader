using Microsoft.Extensions.Options;
using WebApi.Domain;
using WebApi.Interfaces;
using WebApi.Storages;

namespace WebApi.BackgroundServices;

/// <summary>
/// Periodically removes expired Pending/Completing uploads from shared storage + metadata.
/// P4.3: only the node that wins <see cref="IUploadRepository.TryClaimExpiredAsync"/> deletes parts
/// (thin distributed coordination — no Redis/etcd lock service).
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

        var candidates = await repo.GetExpiredPendingAsync(ct);

        if (candidates.Count == 0)
        {
            _logger.LogDebug("Orphan cleanup: no expired sessions found");
            return;
        }

        _logger.LogInformation(
            "Orphan cleanup: {Count} expired candidate(s); claiming via CAS", candidates.Count);

        var claimed = 0;
        var skipped = 0;

        foreach (var session in candidates)
        {
            try
            {
                // Thin distributed lock: only one node transitions to Expired.
                var won = await repo.TryClaimExpiredAsync(session.Id, ct);
                if (!won)
                {
                    skipped++;
                    _logger.LogDebug(
                        "Orphan cleanup: skip {UploadId} (another node claimed or already terminal)",
                        session.Id);
                    continue;
                }

                claimed++;

                // Winner deletes shared part folder (safe on shared volume).
                await storage.DeleteTempFolderAsync(session.Id, ct);

                _logger.LogInformation(
                    "Cleaned orphan upload {UploadId} ({FileName}, was {Status})",
                    session.Id, session.FileName, session.Status);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to clean orphan upload {UploadId}", session.Id);
            }
        }

        _logger.LogInformation(
            "Orphan cleanup cycle done: claimed={Claimed}, skipped={Skipped}", claimed, skipped);
    }
}
