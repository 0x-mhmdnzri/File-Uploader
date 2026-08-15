using WebApi.Domain;

namespace WebApi.Interfaces;

public interface IUploadService
{
    /// <summary>
    /// Creates a pending session, or returns an existing Completed object when content matches
    /// (SHA-256 + size + final file present on shared store).
    /// </summary>
    Task<InitiateResult> InitiateAsync(
        string fileName,
        long totalSize,
        int chunkSize,
        string? contentType = null,
        string? checksum = null,
        string? clientIp = null,
        CancellationToken ct = default);

    Task EnsureCanAcceptChunkAsync(Guid uploadId, int chunkIndex, CancellationToken ct = default);

    Task MarkChunkReceivedAsync(Guid uploadId, int chunkIndex, CancellationToken ct = default);

    Task<string> CompleteAsync(Guid uploadId, string? checksum = null, CancellationToken ct = default);

    Task AbortAsync(Guid uploadId, CancellationToken ct = default);

    Task<UploadSession?> GetStatusAsync(Guid uploadId, CancellationToken ct = default);
}
