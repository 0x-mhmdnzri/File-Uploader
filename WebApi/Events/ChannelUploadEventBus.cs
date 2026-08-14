using System.Threading.Channels;

namespace WebApi.Events;

public class ChannelUploadEventBus : IUploadEventBus
{
    private readonly Channel<UploadEvent> _channel;

    public ChannelUploadEventBus()
    {
        _channel = Channel.CreateUnbounded<UploadEvent>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
    }

    public ValueTask PublishAsync(UploadEvent @event, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(@event, cancellationToken);
    }

    public async IAsyncEnumerable<UploadEvent> SubscribeAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }
}
