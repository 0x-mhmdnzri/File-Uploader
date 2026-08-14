using WebApi.Domain.Events;

namespace WebApi.Interfaces;

/// <summary>
/// Outbound port: publish upload lifecycle events to the outside world.
/// Default adapter logs; replace with message-bus / webhook adapter in the host.
/// </summary>
public interface IUploadEventPublisher
{
    Task PublishCompletedAsync(UploadCompletedEvent @event, CancellationToken ct = default);
    Task PublishAbortedAsync(UploadAbortedEvent @event, CancellationToken ct = default);
    Task PublishFailedAsync(UploadFailedEvent @event, CancellationToken ct = default);
}
