namespace WebApi.Events;

public interface IUploadEventBus
{
    ValueTask PublishAsync(UploadEvent @event, CancellationToken cancellationToken = default);
    IAsyncEnumerable<UploadEvent> SubscribeAsync(CancellationToken cancellationToken = default);
}

public record UploadEvent(Guid UploadId, string EventType, string? ClientIp = null, object? Data = null);
