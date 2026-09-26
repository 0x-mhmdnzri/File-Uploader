namespace WebApi.Metrics;

public interface IUploadMetrics
{
    void RecordInitiated();
    void RecordCompleted(long bytes);
    void RecordFailed();
    void RecordAborted();
    void RecordChunkUploaded();

    /// <summary>Record duration of a single chunk PUT (server-side), in milliseconds.</summary>
    void RecordChunkPutDuration(double milliseconds);

    /// <summary>Record duration of a complete job (verify+merge+hash), in milliseconds.</summary>
    void RecordCompleteJobDuration(double milliseconds);

    UploadMetricsSnapshot Snapshot();
}

public sealed class UploadMetricsSnapshot
{
    public long Initiated { get; init; }
    public long Completed { get; init; }
    public long Failed { get; init; }
    public long Aborted { get; init; }
    public long ChunksUploaded { get; init; }
    public long BytesCompleted { get; init; }
    public DateTimeOffset Since { get; init; }

    /// <summary>Approximate p50 of recent chunk PUT durations (ms). -1 if no samples.</summary>
    public double ChunkPutP50Ms { get; init; } = -1;

    /// <summary>Approximate p95 of recent chunk PUT durations (ms). -1 if no samples.</summary>
    public double ChunkPutP95Ms { get; init; } = -1;

    /// <summary>Approximate p50 of recent complete-job durations (ms). -1 if no samples.</summary>
    public double CompleteJobP50Ms { get; init; } = -1;

    /// <summary>Approximate p95 of recent complete-job durations (ms). -1 if no samples.</summary>
    public double CompleteJobP95Ms { get; init; } = -1;
}
