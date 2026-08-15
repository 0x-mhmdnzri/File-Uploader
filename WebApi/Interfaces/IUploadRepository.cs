using WebApi.Domain;

namespace WebApi.Interfaces;

public interface IUploadRepository
{
    Task AddAsync(UploadSession session, CancellationToken ct = default);
    Task<UploadSession?> GetAsync(Guid id, CancellationToken ct = default);
    Task UpdateAsync(UploadSession session, CancellationToken ct = default);
    Task DeleteAsync(UploadSession session, CancellationToken ct = default);

    Task<IReadOnlyList<UploadSession>> GetExpiredPendingAsync(CancellationToken ct = default);

    /// <summary>
    /// Content-addressed lookup: Completed session with same SHA-256 and total size (newest first).
    /// Shared across nodes via Postgres/Sqlite — works regardless of which node originally uploaded.
    /// </summary>
    Task<UploadSession?> FindCompletedByContentAsync(
        string checksumSha256Hex,
        long totalSize,
        CancellationToken ct = default);

    Task<int> CountActivePendingByIpAsync(string clientIp, CancellationToken ct = default);

    Task<long> SumCompletedBytesAsync(CancellationToken ct = default);
    Task<long> SumCompletedBytesByIpAsync(string clientIp, CancellationToken ct = default);
    Task<long> SumActivePendingBytesAsync(CancellationToken ct = default);
    Task<long> SumActivePendingBytesByIpAsync(string clientIp, CancellationToken ct = default);

    Task<bool> TryBeginCompleteAsync(Guid id, CancellationToken ct = default);

    Task<bool> TryFinishCompleteAsync(
        Guid id,
        string finalFileName,
        string? checksum,
        CancellationToken ct = default);

    Task<bool> TryFailCompleteAsync(Guid id, string? checksum, CancellationToken ct = default);

    Task<bool> TryClaimExpiredAsync(Guid id, CancellationToken ct = default);

    Task<bool> TryAbortAsync(Guid id, CancellationToken ct = default);
}
