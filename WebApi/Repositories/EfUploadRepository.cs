using Microsoft.EntityFrameworkCore;
using WebApi.Data;
using WebApi.Domain;
using WebApi.Interfaces;

namespace WebApi.Repositories;

public class EfUploadRepository : IUploadRepository
{
    private readonly AppDbContext _db;

    public EfUploadRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(UploadSession session, CancellationToken ct = default)
    {
        _db.UploadSessions.Add(session);
        await _db.SaveChangesAsync(ct);
    }

    public Task<UploadSession?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return _db.UploadSessions.FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task UpdateAsync(UploadSession session, CancellationToken ct = default)
    {
        _db.UploadSessions.Update(session);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(UploadSession session, CancellationToken ct = default)
    {
        _db.UploadSessions.Remove(session);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<UploadSession>> GetExpiredPendingAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.UploadSessions
            .Where(x => x.Status == UploadStatus.Pending && x.ExpiresAt <= now)
            .ToListAsync(ct);
    }

    public Task<int> CountActivePendingByIpAsync(string clientIp, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return _db.UploadSessions.CountAsync(
            x => x.ClientIp == clientIp
                 && x.Status == UploadStatus.Pending
                 && x.ExpiresAt > now,
            ct);
    }

    public async Task<long> SumCompletedBytesAsync(CancellationToken ct = default)
    {
        return await _db.UploadSessions
            .Where(x => x.Status == UploadStatus.Completed)
            .SumAsync(x => (long?)x.TotalSize, ct) ?? 0L;
    }

    public async Task<long> SumCompletedBytesByIpAsync(string clientIp, CancellationToken ct = default)
    {
        return await _db.UploadSessions
            .Where(x => x.Status == UploadStatus.Completed && x.ClientIp == clientIp)
            .SumAsync(x => (long?)x.TotalSize, ct) ?? 0L;
    }

    public async Task<long> SumActivePendingBytesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.UploadSessions
            .Where(x => x.Status == UploadStatus.Pending && x.ExpiresAt > now)
            .SumAsync(x => (long?)x.TotalSize, ct) ?? 0L;
    }

    public async Task<long> SumActivePendingBytesByIpAsync(string clientIp, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.UploadSessions
            .Where(x => x.Status == UploadStatus.Pending && x.ExpiresAt > now && x.ClientIp == clientIp)
            .SumAsync(x => (long?)x.TotalSize, ct) ?? 0L;
    }
}
