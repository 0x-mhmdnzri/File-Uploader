using System.Threading.Channels;

namespace WebApi.Services;

public sealed class ChannelCompleteJobQueue : ICompleteJobQueue
{
    private readonly Channel<CompleteJob> _channel = Channel.CreateUnbounded<CompleteJob>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    public ValueTask EnqueueAsync(CompleteJob job, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(job, ct);

    public IAsyncEnumerable<CompleteJob> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
