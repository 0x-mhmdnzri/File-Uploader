using WebApi.Domain;
using WebApi.Interfaces;

namespace WebApi.Services;

/// <summary>
/// Lightweight periodic marker for expired Pending sessions.
/// Disk cleanup is owned by OrphanCleanupService; this only flips status when needed.
/// </summary>
public class UploadCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UploadCleanupService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public UploadCleanupService(IServiceScopeFactory scopeFactory, ILogger<UploadCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IUploadRepository>();

                var expired = await repo.GetExpiredPendingAsync(stoppingToken);

                foreach (var session in expired)
                {
                    if (session.Status != UploadStatus.Pending)
                        continue;

                    session.Status = UploadStatus.Expired;
                    await repo.UpdateAsync(session, stoppingToken);
                    _logger.LogInformation("Marked upload session {UploadId} as Expired", session.Id);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during upload cleanup");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
