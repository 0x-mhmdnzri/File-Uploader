namespace WebApi.Interfaces;

public interface IFileStorage
{
    Task EnsureDirectoriesAsync(CancellationToken ct = default);

    Task SaveChunkAsync(Guid uploadId, int chunkIndex, Stream data, CancellationToken ct = default);

    /// <summary>
    /// Pre-allocates the final file and writes each part at its byte offset in parallel.
    /// Returns the final file path.
    /// </summary>
    Task<string> MergeAsync(
        Guid uploadId,
        string fileName,
        int totalChunks,
        long totalSize,
        int chunkSize,
        CancellationToken ct = default);

    /// <summary>
    /// Computes SHA-256 of a file on disk and returns lowercase hex string.
    /// </summary>
    Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default);

    Task DeleteTempFolderAsync(Guid uploadId, CancellationToken ct = default);

    Task DeleteFinalFileAsync(string fileName, CancellationToken ct = default);

    Task<string> GetTempFolderAsync(Guid uploadId);

    Task<bool> ChunkExistsAsync(Guid uploadId, int chunkIndex);

    /// <summary>
    /// Returns the set of chunk indexes that actually exist on disk for this upload.
    /// This is the source of truth under concurrent parallel uploads.
    /// </summary>
    Task<IReadOnlyCollection<int>> GetExistingChunkIndexesAsync(Guid uploadId, CancellationToken ct = default);

    /// <summary>
    /// Parallel verification: discovers missing indexes and accumulates on-disk byte size.
    /// Uses ConcurrentBag + Interlocked. Returns (missing indexes, total bytes found).
    /// </summary>
    Task<(IReadOnlyCollection<int> Missing, long BytesOnDisk)> VerifyChunksParallelAsync(
        Guid uploadId,
        int totalChunks,
        CancellationToken ct = default);
}
