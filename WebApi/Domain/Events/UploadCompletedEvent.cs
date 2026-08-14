namespace WebApi.Domain.Events;

/// <summary>
/// Fired after a file has been successfully merged and marked Completed.
/// Other bounded contexts (virus scan, indexing, domain services) can react via an adapter.
/// </summary>
public sealed record UploadCompletedEvent(
    Guid UploadId,
    string FileName,
    string FinalFileName,
    long TotalSize,
    string? ContentType,
    string? Checksum,
    string? ClientIp,
    DateTime CompletedAtUtc);
