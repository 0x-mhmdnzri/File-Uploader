using WebApi.Domain;

namespace WebApi.Interfaces;

public interface IUploadService
{
    Task<UploadSession> InitiateAsync(string fileName, long totalSize, int chunkSize, string? contentType = null, CancellationToken ct = default);

    Task MarkChunkReceivedAsync(Guid uploadId, int chunkIndex, CancellationToken ct = default);

    Task<string> CompleteAsync(Guid uploadId, CancellationToken ct = default);

    Task AbortAsync(Guid uploadId, CancellationToken ct = default);

    Task<UploadSession?> GetStatusAsync(Guid uploadId, CancellationToken ct = default);
}
