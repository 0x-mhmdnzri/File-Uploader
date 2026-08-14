using WebApi.Domain.Events;

namespace WebApi.Interfaces;

/// <summary>
/// Consumer of upload lifecycle events (in-process or bridge to external systems).
/// </summary>
public interface IUploadEventHandler
{
    Task HandleCompletedAsync(UploadCompletedEvent @event, CancellationToken ct = default);
    Task HandleAbortedAsync(UploadAbortedEvent @event, CancellationToken ct = default);
    Task HandleFailedAsync(UploadFailedEvent @event, CancellationToken ct = default);
}
