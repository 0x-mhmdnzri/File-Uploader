using System.Diagnostics.Metrics;

namespace WebApi.Metrics;

/// <summary>
/// In-process counters for upload activity (also exposes System.Diagnostics.Metrics).
/// </summary>
public sealed class UploadMetrics : IUploadMetrics
{
    private readonly DateTimeOffset _since = DateTimeOffset.UtcNow;

    private long _initiated;
    private long _completed;
    private long _failed;
    private long _aborted;
    private long _chunksUploaded;
    private long _bytesCompleted;

    private readonly Counter<long> _initiatedCounter;
    private readonly Counter<long> _completedCounter;
    private readonly Counter<long> _failedCounter;
    private readonly Counter<long> _abortedCounter;
    private readonly Counter<long> _chunksCounter;
    private readonly Counter<long> _bytesCounter;

    public UploadMetrics()
    {
        var meter = new Meter("FileUploader", "1.0");
        _initiatedCounter = meter.CreateCounter<long>("uploads.initiated");
        _completedCounter = meter.CreateCounter<long>("uploads.completed");
        _failedCounter = meter.CreateCounter<long>("uploads.failed");
        _abortedCounter = meter.CreateCounter<long>("uploads.aborted");
        _chunksCounter = meter.CreateCounter<long>("uploads.chunks");
        _bytesCounter = meter.CreateCounter<long>("uploads.bytes_completed");
    }

    public void RecordInitiated()
    {
        Interlocked.Increment(ref _initiated);
        _initiatedCounter.Add(1);
    }

    public void RecordCompleted(long bytes)
    {
        Interlocked.Increment(ref _completed);
        Interlocked.Add(ref _bytesCompleted, bytes);
        _completedCounter.Add(1);
        _bytesCounter.Add(bytes);
    }

    public void RecordFailed()
    {
        Interlocked.Increment(ref _failed);
        _failedCounter.Add(1);
    }

    public void RecordAborted()
    {
        Interlocked.Increment(ref _aborted);
        _abortedCounter.Add(1);
    }

    public void RecordChunkUploaded()
    {
        Interlocked.Increment(ref _chunksUploaded);
        _chunksCounter.Add(1);
    }

    public UploadMetricsSnapshot Snapshot() => new()
    {
        Initiated = Interlocked.Read(ref _initiated),
        Completed = Interlocked.Read(ref _completed),
        Failed = Interlocked.Read(ref _failed),
        Aborted = Interlocked.Read(ref _aborted),
        ChunksUploaded = Interlocked.Read(ref _chunksUploaded),
        BytesCompleted = Interlocked.Read(ref _bytesCompleted),
        Since = _since
    };
}
