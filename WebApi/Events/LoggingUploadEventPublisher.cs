using WebApi.Domain.Events;
using WebApi.Interfaces;

namespace WebApi.Events;

/// <summary>
/// Default outbound adapter: structured log only.
/// Swap for RabbitMQ / Kafka / HTTP webhook without touching UploadService.
/// </summary>
public sealed class LoggingUploadEventPublisher : IUploadEventPublisher
{
    private readonly ILogger<LoggingUploadEventPublisher> _logger;

    public LoggingUploadEventPublisher(ILogger<LoggingUploadEventPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishCompletedAsync(UploadCompletedEvent @event, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Event UploadCompleted UploadId={UploadId} File={FinalFileName} Size={TotalSize} Checksum={Checksum}",
            @event.UploadId, @event.FinalFileName, @event.TotalSize, @event.Checksum ?? "-");
        return Task.CompletedTask;
    }

    public Task PublishAbortedAsync(UploadAbortedEvent @event, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Event UploadAborted UploadId={UploadId} File={FileName}",
            @event.UploadId, @event.FileName);
        return Task.CompletedTask;
    }

    public Task PublishFailedAsync(UploadFailedEvent @event, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "Event UploadFailed UploadId={UploadId} File={FileName} Reason={Reason}",
            @event.UploadId, @event.FileName, @event.Reason);
        return Task.CompletedTask;
    }
}
