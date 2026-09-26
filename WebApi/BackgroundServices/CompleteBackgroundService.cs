using Microsoft.Extensions.Options;
using WebApi.Storages;

namespace WebApi.BackgroundServices;

/// <summary>
/// Processes Complete jobs off the request path so clients get a fast 202
/// and can poll /status while merge + SHA-256 run (perf 1.4).
/// </summary>
public sealed class CompleteBackgroundService : BackgroundService
{
    private readonly ICompleteJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CompleteBackgroundService> _logger;

    public CompleteBackgroundService(
        ICompleteJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<CompleteBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CompleteBackgroundService started");

        await foreach (var job in _queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IUploadService>();
                await service.ProcessCompleteJobAsync(job, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing complete job {UploadId}", job.UploadId);
            }
        }

        _logger.LogInformation("CompleteBackgroundService stopped");
    }
}
