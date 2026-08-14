using System.Threading.Channels;

namespace WebApi.Events;

/// <summary>
/// Bounded in-process bus. Publish is non-blocking (waits if full).
/// </summary>
internal sealed class ChannelUploadEventBus
{
    private readonly Channel<UploadEventEnvelope> _channel;

    public ChannelUploadEventBus()
    {
        _channel = Channel.CreateBounded<UploadEventEnvelope>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelWriter<UploadEventEnvelope> Writer => _channel.Writer;
    public ChannelReader<UploadEventEnvelope> Reader => _channel.Reader;
}
