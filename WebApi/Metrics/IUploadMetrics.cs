namespace WebApi.Metrics;

public interface IUploadMetrics
{
    void RecordInitiated();
    void RecordCompleted(long bytes);
    void RecordFailed();
    void RecordAborted();
    void RecordChunkUploaded();

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
}
