using WebApi.Domain.Events;
using WebApi.Interfaces;

namespace WebApi.Events;

/// <summary>
/// Background reader of the channel bus; dispatches to all registered handlers.
/// Handler failures are isolated and never affect the upload pipeline.
/// </summary>
public sealed class UploadEventDispatcherService : BackgroundService
{
    private readonly ChannelUploadEventBus _bus;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UploadEventDispatcherService> _logger;

    public UploadEventDispatcherService(
        ChannelUploadEventBus bus,
        IServiceScopeFactory scopeFactory,
        ILogger<UploadEventDispatcherService> logger)
    {
        _bus = bus;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Upload event dispatcher started");

        await foreach (var envelope in _bus.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await DispatchAsync(envelope, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error dispatching upload event {Kind}", envelope.Kind);
            }
        }
    }

    private async Task DispatchAsync(UploadEventEnvelope envelope, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IUploadEventHandler>().ToList();

        if (handlers.Count == 0)
        {
            _logger.LogDebug("No upload event handlers registered for {Kind}", envelope.Kind);
            return;
        }

        foreach (var handler in handlers)
        {
            try
            {
                switch (envelope.Kind)
                {
                    case UploadEventKind.Completed:
                        await handler.HandleCompletedAsync((UploadCompletedEvent)envelope.Payload, ct);
                        break;
                    case UploadEventKind.Aborted:
                        await handler.HandleAbortedAsync((UploadAbortedEvent)envelope.Payload, ct);
                        break;
                    case UploadEventKind.Failed:
                        await handler.HandleFailedAsync((UploadFailedEvent)envelope.Payload, ct);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Handler {Handler} failed for {Kind}",
                    handler.GetType().Name, envelope.Kind);
            }
        }
    }
}
