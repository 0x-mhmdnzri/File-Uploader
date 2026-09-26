using System.Diagnostics.Metrics;

namespace WebApi.Metrics;

/// <summary>
/// In-process counters + lightweight duration histograms for upload activity
/// (also exposes System.Diagnostics.Metrics). Perf 5.2.
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
    private readonly Histogram<double> _chunkPutHistogram;
    private readonly Histogram<double> _completeJobHistogram;

    // Ring buffers for cheap p50/p95 in /api/metrics (last N samples)
    private const int SampleCapacity = 256;
    private readonly double[] _chunkPutSamples = new double[SampleCapacity];
    private readonly double[] _completeJobSamples = new double[SampleCapacity];
    private int _chunkPutIndex;
    private int _chunkPutCount;
    private int _completeJobIndex;
    private int _completeJobCount;
    private readonly object _sampleLock = new();

    public UploadMetrics()
    {
        var meter = new Meter("FileUploader", "1.0");
        _initiatedCounter = meter.CreateCounter<long>("uploads.initiated");
        _completedCounter = meter.CreateCounter<long>("uploads.completed");
        _failedCounter = meter.CreateCounter<long>("uploads.failed");
        _abortedCounter = meter.CreateCounter<long>("uploads.aborted");
        _chunksCounter = meter.CreateCounter<long>("uploads.chunks");
        _bytesCounter = meter.CreateCounter<long>("uploads.bytes_completed");
        _chunkPutHistogram = meter.CreateHistogram<double>("uploads.chunk_put_duration_ms", unit: "ms");
        _completeJobHistogram = meter.CreateHistogram<double>("uploads.complete_job_duration_ms", unit: "ms");
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

    public void RecordChunkPutDuration(double milliseconds)
    {
        if (milliseconds < 0) return;
        _chunkPutHistogram.Record(milliseconds);
        lock (_sampleLock)
        {
            _chunkPutSamples[_chunkPutIndex] = milliseconds;
            _chunkPutIndex = (_chunkPutIndex + 1) % SampleCapacity;
            if (_chunkPutCount < SampleCapacity) _chunkPutCount++;
        }
    }

    public void RecordCompleteJobDuration(double milliseconds)
    {
        if (milliseconds < 0) return;
        _completeJobHistogram.Record(milliseconds);
        lock (_sampleLock)
        {
            _completeJobSamples[_completeJobIndex] = milliseconds;
            _completeJobIndex = (_completeJobIndex + 1) % SampleCapacity;
            if (_completeJobCount < SampleCapacity) _completeJobCount++;
        }
    }

    public UploadMetricsSnapshot Snapshot()
    {
        double chunkP50 = -1, chunkP95 = -1, completeP50 = -1, completeP95 = -1;
        lock (_sampleLock)
        {
            (chunkP50, chunkP95) = Percentiles(_chunkPutSamples, _chunkPutCount);
            (completeP50, completeP95) = Percentiles(_completeJobSamples, _completeJobCount);
        }

        return new UploadMetricsSnapshot
        {
            Initiated = Interlocked.Read(ref _initiated),
            Completed = Interlocked.Read(ref _completed),
            Failed = Interlocked.Read(ref _failed),
            Aborted = Interlocked.Read(ref _aborted),
            ChunksUploaded = Interlocked.Read(ref _chunksUploaded),
            BytesCompleted = Interlocked.Read(ref _bytesCompleted),
            Since = _since,
            ChunkPutP50Ms = chunkP50,
            ChunkPutP95Ms = chunkP95,
            CompleteJobP50Ms = completeP50,
            CompleteJobP95Ms = completeP95
        };
    }

    private static (double p50, double p95) Percentiles(double[] ring, int count)
    {
        if (count <= 0) return (-1, -1);
        var sorted = new double[count];
        Array.Copy(ring, sorted, count);
        Array.Sort(sorted);
        return (sorted[PercentileIndex(count, 0.50)], sorted[PercentileIndex(count, 0.95)]);
    }

    private static int PercentileIndex(int count, double p)
        => Math.Clamp((int)Math.Ceiling(p * count) - 1, 0, count - 1);
}
