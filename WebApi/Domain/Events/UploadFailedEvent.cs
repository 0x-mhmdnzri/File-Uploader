namespace WebApi.Domain.Events;

public sealed record UploadFailedEvent(
    Guid UploadId,
    string FileName,
    string Reason,
    string? ClientIp,
    DateTime FailedAtUtc);
