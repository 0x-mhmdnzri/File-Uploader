namespace WebApi.Services;

/// <summary>
/// Job enqueued after a node wins the Complete CAS lease.
/// Processed by <see cref="CompleteBackgroundService"/>.
/// </summary>
public sealed record CompleteJob(
    Guid UploadId,
    string? ClientChecksum,
    DateTimeOffset EnqueuedAt);
