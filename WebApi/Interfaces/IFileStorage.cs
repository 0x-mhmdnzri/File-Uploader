namespace WebApi.Interfaces;

public interface IFileStorage
{
    Task EnsureDirectoriesAsync(CancellationToken ct = default);

    Task SaveChunkAsync(Guid uploadId, int chunkIndex, Stream data, CancellationToken ct = default);

    /// <summary>
    /// Sequentially merges all parts into the final file. Returns the final file path.
    /// </summary>
    Task<string> MergeAsync(Guid uploadId, string fileName, int totalChunks, CancellationToken ct = default);

    /// <summary>
    /// Computes SHA-256 of a file on disk and returns lowercase hex string.
    /// </summary>
    Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default);

    Task DeleteTempFolderAsync(Guid uploadId, CancellationToken ct = default);

    Task DeleteFinalFileAsync(string fileName, CancellationToken ct = default);

    Task<string> GetTempFolderAsync(Guid uploadId);

    Task<bool> ChunkExistsAsync(Guid uploadId, int chunkIndex);
}
