using WebApi.Domain;

namespace WebApi.Interfaces;

public interface IUploadRepository
{
    Task AddAsync(UploadSession session, CancellationToken ct = default);
    Task<UploadSession?> GetAsync(Guid id, CancellationToken ct = default);
    Task UpdateAsync(UploadSession session, CancellationToken ct = default);
    Task DeleteAsync(UploadSession session, CancellationToken ct = default);

    Task<IReadOnlyList<UploadSession>> GetExpiredPendingAsync(CancellationToken ct = default);

    Task<int> CountActivePendingByIpAsync(string clientIp, CancellationToken ct = default);

    Task<long> SumCompletedBytesAsync(CancellationToken ct = default);
    Task<long> SumCompletedBytesByIpAsync(string clientIp, CancellationToken ct = default);
    Task<long> SumActivePendingBytesAsync(CancellationToken ct = default);
    Task<long> SumActivePendingBytesByIpAsync(string clientIp, CancellationToken ct = default);

    /// <summary>
    /// CAS: Pending → Completing. Returns true if this caller won the merge lease.
    /// </summary>
    Task<bool> TryBeginCompleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// CAS: Completing → Completed (with final metadata).
    /// </summary>
    Task<bool> TryFinishCompleteAsync(
        Guid id,
        string finalFileName,
        string? checksum,
        CancellationToken ct = default);

    /// <summary>
    /// CAS: Completing → Failed.
    /// </summary>
    Task<bool> TryFailCompleteAsync(Guid id, string? checksum, CancellationToken ct = default);
}
