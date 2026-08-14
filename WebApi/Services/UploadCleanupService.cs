using WebApi.Domain;
using WebApi.Events;
using WebApi.Interfaces;

namespace WebApi.Services;

public class UploadCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<UploadCleanupService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

    public UploadCleanupService(IServiceProvider serviceProvider, ILogger<UploadCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IUploadRepository>();
                var eventBus = scope.ServiceProvider.GetRequiredService<IUploadEventBus>();

                var now = DateTime.UtcNow;
                var expired = await repo.GetExpiredPendingAsync(now);

                foreach (var session in expired)
                {
                    session.Status = UploadStatus.Expired;
                    await repo.UpdateAsync(session);
                    await eventBus.PublishAsync(new UploadEvent(session.Id, "Expired", session.ClientIp), stoppingToken);
                    _logger.LogInformation("Expired upload session {UploadId}", session.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during upload cleanup");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
