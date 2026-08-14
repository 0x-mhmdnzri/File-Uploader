namespace WebApi.Events;

public enum UploadEventKind
{
    Completed,
    Aborted,
    Failed
}

/// <summary>
/// Discriminated envelope for the in-process channel bus.
/// </summary>
public sealed class UploadEventEnvelope
{
    public UploadEventKind Kind { get; init; }
    public object Payload { get; init; } = null!;
}
