using WebApi.Domain.Events;
using WebApi.Interfaces;

namespace WebApi.Events;

/// <summary>
/// Fans out to multiple outbound adapters (log + bus + webhook, …).
/// </summary>
public sealed class CompositeUploadEventPublisher : IUploadEventPublisher
{
    private readonly IReadOnlyList<IUploadEventPublisher> _publishers;
    private readonly ILogger<CompositeUploadEventPublisher> _logger;

    public CompositeUploadEventPublisher(
        IEnumerable<IUploadEventPublisher> publishers,
        ILogger<CompositeUploadEventPublisher> logger)
    {
        _publishers = publishers.ToList();
        _logger = logger;
    }

    public Task PublishCompletedAsync(UploadCompletedEvent @event, CancellationToken ct = default)
        => PublishAllAsync(p => p.PublishCompletedAsync(@event, ct));

    public Task PublishAbortedAsync(UploadAbortedEvent @event, CancellationToken ct = default)
        => PublishAllAsync(p => p.PublishAbortedAsync(@event, ct));

    public Task PublishFailedAsync(UploadFailedEvent @event, CancellationToken ct = default)
        => PublishAllAsync(p => p.PublishFailedAsync(@event, ct));

    private async Task PublishAllAsync(Func<IUploadEventPublisher, Task> action)
    {
        foreach (var publisher in _publishers)
        {
            try
            {
                await action(publisher);
            }
            catch (Exception ex)
            {
                // Never fail the upload path because of a downstream event adapter
                _logger.LogError(ex, "Outbound event publisher {Publisher} failed", publisher.GetType().Name);
            }
        }
    }
}
