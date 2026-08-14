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

    /// <summary>
    /// Sum of TotalSize for Completed sessions (global stored data accounting).
    /// </summary>
    Task<long> SumCompletedBytesAsync(CancellationToken ct = default);

    /// <summary>
    /// Sum of TotalSize for Completed sessions from a client IP.
    /// </summary>
    Task<long> SumCompletedBytesByIpAsync(string clientIp, CancellationToken ct = default);

    /// <summary>
    /// Sum of TotalSize for active Pending sessions (reserved quota).
    /// </summary>
    Task<long> SumActivePendingBytesAsync(CancellationToken ct = default);

    Task<long> SumActivePendingBytesByIpAsync(string clientIp, CancellationToken ct = default);
}
