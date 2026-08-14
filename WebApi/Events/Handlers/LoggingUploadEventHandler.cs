using WebApi.Domain.Events;
using WebApi.Interfaces;

namespace WebApi.Events.Handlers;

public sealed class LoggingUploadEventHandler : IUploadEventHandler
{
    private readonly ILogger<LoggingUploadEventHandler> _logger;

    public LoggingUploadEventHandler(ILogger<LoggingUploadEventHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleCompletedAsync(UploadCompletedEvent @event, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Event UploadCompleted UploadId={UploadId} File={FinalFileName} Size={TotalSize} Checksum={Checksum}",
            @event.UploadId, @event.FinalFileName, @event.TotalSize, @event.Checksum ?? "-");
        return Task.CompletedTask;
    }

    public Task HandleAbortedAsync(UploadAbortedEvent @event, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Event UploadAborted UploadId={UploadId} File={FileName}",
            @event.UploadId, @event.FileName);
        return Task.CompletedTask;
    }

    public Task HandleFailedAsync(UploadFailedEvent @event, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "Event UploadFailed UploadId={UploadId} File={FileName} Reason={Reason}",
            @event.UploadId, @event.FileName, @event.Reason);
        return Task.CompletedTask;
    }
}
