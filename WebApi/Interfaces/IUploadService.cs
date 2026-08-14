using WebApi.Domain;

namespace WebApi.Interfaces;

public interface IUploadService
{
<<<<<<< HEAD
    Task<UploadSession> InitiateAsync(string fileName, long totalSize, int chunkSize, string? clientIp = null);
    Task MarkChunkReceivedAsync(Guid uploadId, int chunkIndex);
    Task MergeChunksAsync(Guid uploadId);
    Task<UploadSession?> GetStatusAsync(Guid uploadId);
=======
    Task<UploadSession> InitiateAsync(
        string fileName,
        long totalSize,
        int chunkSize,
        string? contentType = null,
        string? checksum = null,
        string? clientIp = null,
        CancellationToken ct = default);

    Task MarkChunkReceivedAsync(Guid uploadId, int chunkIndex, CancellationToken ct = default);

    /// <summary>
    /// Merges chunks, optionally verifies checksum, and marks session Completed.
    /// Returns final file path.
    /// </summary>
    Task<string> CompleteAsync(Guid uploadId, string? checksum = null, CancellationToken ct = default);

    Task AbortAsync(Guid uploadId, CancellationToken ct = default);

    Task<UploadSession?> GetStatusAsync(Guid uploadId, CancellationToken ct = default);
>>>>>>> origin/main
}
