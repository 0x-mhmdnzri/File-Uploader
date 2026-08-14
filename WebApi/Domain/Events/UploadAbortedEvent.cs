namespace WebApi.Domain.Events;

public sealed record UploadAbortedEvent(
    Guid UploadId,
    string FileName,
    string? ClientIp,
    DateTime AbortedAtUtc);
