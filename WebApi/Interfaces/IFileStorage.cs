namespace WebApi.Interfaces;

public interface IFileStorage
{
    Task EnsureDirectoriesAsync(CancellationToken ct = default);

    Task SaveChunkAsync(Guid uploadId, int chunkIndex, Stream data, CancellationToken ct = default);

    /// <summary>
    /// Deletes a single part file if present (e.g. after CRC mismatch).
    /// </summary>
    Task DeleteChunkAsync(Guid uploadId, int chunkIndex, CancellationToken ct = default);

    /// <summary>
    /// Merges parts into the final object.
    /// When <paramref name="computeHash"/> is false, skips full-file SHA-256 (much faster for multi-GB).
    /// Returns (final path, lowercase hex SHA-256 or empty when hash skipped).
    /// </summary>
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

    Task<string> GetTempFolderAsync(Guid uploadId);

    Task<bool> ChunkExistsAsync(Guid uploadId, int chunkIndex);

    Task<IReadOnlyCollection<int>> GetExistingChunkIndexesAsync(Guid uploadId, CancellationToken ct = default);

    Task<(IReadOnlyCollection<int> Missing, long BytesOnDisk)> VerifyChunksParallelAsync(
        Guid uploadId,
        int totalChunks,
        CancellationToken ct = default);
}
