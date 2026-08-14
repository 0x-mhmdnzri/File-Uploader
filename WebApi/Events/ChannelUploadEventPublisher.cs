using WebApi.Domain.Events;
using WebApi.Interfaces;

namespace WebApi.Events;

/// <summary>
/// Outbound adapter: enqueues events onto the in-process channel bus.
/// Does not run handlers inline — keeps UploadService fast and decoupled.
/// </summary>
public sealed class ChannelUploadEventPublisher : IUploadEventPublisher
{
    private readonly ChannelUploadEventBus _bus;

    public ChannelUploadEventPublisher(ChannelUploadEventBus bus)
    {
        _bus = bus;
    }

    public async Task PublishCompletedAsync(UploadCompletedEvent @event, CancellationToken ct = default)
    {
        await _bus.Writer.WriteAsync(new UploadEventEnvelope
        {
            Kind = UploadEventKind.Completed,
            Payload = @event
        }, ct);
    }

    public async Task PublishAbortedAsync(UploadAbortedEvent @event, CancellationToken ct = default)
    {
        await _bus.Writer.WriteAsync(new UploadEventEnvelope
        {
            Kind = UploadEventKind.Aborted,
            Payload = @event
        }, ct);
    }

    public async Task PublishFailedAsync(UploadFailedEvent @event, CancellationToken ct = default)
    {
        await _bus.Writer.WriteAsync(new UploadEventEnvelope
        {
            Kind = UploadEventKind.Failed,
            Payload = @event
        }, ct);
    }
}
