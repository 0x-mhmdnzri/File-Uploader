namespace WebApi.Events;

internal enum UploadEventKind
{
    Completed,
    Aborted,
    Failed
}

/// <summary>
/// Discriminated envelope for the in-process channel bus.
/// </summary>
internal sealed class UploadEventEnvelope
{
    public UploadEventKind Kind { get; init; }
    public object Payload { get; init; } = null!;
}
