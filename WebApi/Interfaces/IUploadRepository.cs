using WebApi.Domain;

namespace WebApi.Interfaces;

public interface IUploadRepository
{
    Task AddAsync(UploadSession session, CancellationToken ct = default);
    Task<UploadSession?> GetAsync(Guid id, CancellationToken ct = default);
    Task UpdateAsync(UploadSession session, CancellationToken ct = default);
    Task DeleteAsync(UploadSession session, CancellationToken ct = default);

    /// <summary>
    /// Returns Pending sessions whose ExpiresAt has passed.
    /// </summary>
    Task<IReadOnlyList<UploadSession>> GetExpiredPendingAsync(CancellationToken ct = default);

    /// <summary>
    /// Counts active (non-expired) Pending sessions for a given client IP.
    /// </summary>
    Task<int> CountActivePendingByIpAsync(string clientIp, CancellationToken ct = default);
}
