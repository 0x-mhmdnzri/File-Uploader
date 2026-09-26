using WebApi.Domain;
using WebApi.Services;

namespace WebApi.Interfaces;

public interface IUploadService
{
    /// <summary>
    /// Creates a pending session, or returns an existing Completed object when content matches
    /// (full SHA-256 or sample fingerprint + size + final file present on shared store).
    /// </summary>
    Task<InitiateResult> InitiateAsync(
        string fileName,
        long totalSize,
        int chunkSize,
        string? contentType = null,
        string? checksum = null,
        string? contentFingerprint = null,
        string? clientIp = null,
        CancellationToken ct = default);

    Task EnsureCanAcceptChunkAsync(Guid uploadId, int chunkIndex, CancellationToken ct = default);

    Task MarkChunkReceivedAsync(Guid uploadId, int chunkIndex, CancellationToken ct = default);

    /// <summary>
    /// Acquires the CAS complete lease and enqueues the heavy merge+hash work
    /// to <see cref="CompleteBackgroundService"/> (perf 1.4). Returns quickly with
    /// <see cref="CompleteResult.AcceptedForBackground"/> = true.
    /// </summary>
    Task<CompleteResult> CompleteAsync(Guid uploadId, string? checksum = null, CancellationToken ct = default);

    /// <summary>
    /// Called by the background worker after a job is dequeued.
    /// </summary>
    Task ProcessCompleteJobAsync(CompleteJob job, CancellationToken ct = default);

    Task AbortAsync(Guid uploadId, CancellationToken ct = default);

    Task<UploadSession?> GetStatusAsync(Guid uploadId, CancellationToken ct = default);
}
