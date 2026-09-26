namespace WebApi.Services;

public interface ICompleteJobQueue
{
    ValueTask EnqueueAsync(CompleteJob job, CancellationToken ct = default);
    IAsyncEnumerable<CompleteJob> ReadAllAsync(CancellationToken ct);
}
