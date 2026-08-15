namespace WebApi.Interfaces;

public interface IFileStorage
{
    Task EnsureDirectoriesAsync(CancellationToken ct = default);

    Task SaveChunkAsync(Guid uploadId, int chunkIndex, Stream data, CancellationToken ct = default);

    Task DeleteChunkAsync(Guid uploadId, int chunkIndex, CancellationToken ct = default);

    Task<(string Path, string Sha256Hex)> MergeAsync(
        Guid uploadId,
        string fileName,
        int totalChunks,
        long totalSize,
        int chunkSize,
        bool computeHash = true,
        CancellationToken ct = default);

    Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default);

    Task DeleteTempFolderAsync(Guid uploadId, CancellationToken ct = default);

    Task DeleteFinalFileAsync(string fileName, CancellationToken ct = default);

    /// <summary>
    /// True when the final object exists and (when <paramref name="expectedSize"/> is set) length matches.
    /// Used for content-addressed dedupe before trusting a Completed session row.
    /// </summary>
    Task<bool> FinalObjectExistsAsync(string fileName, long? expectedSize = null, CancellationToken ct = default);

    Task<string> GetTempFolderAsync(Guid uploadId);

    Task<bool> ChunkExistsAsync(Guid uploadId, int chunkIndex);

    Task<IReadOnlyCollection<int>> GetExistingChunkIndexesAsync(Guid uploadId, CancellationToken ct = default);

    Task<(IReadOnlyCollection<int> Missing, long BytesOnDisk)> VerifyChunksParallelAsync(
        Guid uploadId,
        int totalChunks,
        CancellationToken ct = default);
}
