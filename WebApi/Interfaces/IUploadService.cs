using WebApi.Domain;

namespace WebApi.Interfaces;

public interface IUploadService
{
    Task<UploadSession> InitiateAsync(string fileName, long totalSize, int chunkSize);
    Task MarkChunkReceivedAsync(Guid uploadId, int chunkIndex);
    Task MergeChunksAsync(Guid uploadId);
    Task<UploadSession> GetStatusAsync(Guid uploadId);
}