using WebApi.Domain;

namespace WebApi.Interfaces;

public interface IUploadService
{
    Task<UploadSession> InitiateAsync(
        string fileName,
        long totalSize,
        int chunkSize,
        string? contentType = null,
        string? checksum = null,
        string? clientIp = null,
        CancellationToken ct = default);

    /// <summary>
    /// Validates session is Pending and not expired. Uses session cache when possible.
    /// Call before writing a chunk to disk.
    /// </summary>
    Task EnsureCanAcceptChunkAsync(Guid uploadId, int chunkIndex, CancellationToken ct = default);

    Task MarkChunkReceivedAsync(Guid uploadId, int chunkIndex, CancellationToken ct = default);

    /// <summary>
    /// Merges chunks, verifies checksum when provided, marks Completed.
    /// Returns final file path.
    /// </summary>
    Task<string> CompleteAsync(Guid uploadId, string? checksum = null, CancellationToken ct = default);

    Task AbortAsync(Guid uploadId, CancellationToken ct = default);

    Task<UploadSession?> GetStatusAsync(Guid uploadId, CancellationToken ct = default);
}
