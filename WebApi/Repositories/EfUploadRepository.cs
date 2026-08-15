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
        session.Version = 0;
        _db.UploadSessions.Add(session);
        await _db.SaveChangesAsync(ct);
    }

    public Task<UploadSession?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return _db.UploadSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task UpdateAsync(UploadSession session, CancellationToken ct = default)
    {
        var tracked = await _db.UploadSessions.FirstOrDefaultAsync(x => x.Id == session.Id, ct)
                      ?? throw new InvalidOperationException($"Session {session.Id} not found");

        if (tracked.Version != session.Version)
            throw new DbUpdateConcurrencyException("Session version conflict.");

        tracked.FileName = session.FileName;
        tracked.FinalFileName = session.FinalFileName;
        tracked.TotalSize = session.TotalSize;
        tracked.ChunkSize = session.ChunkSize;
        tracked.TotalChunks = session.TotalChunks;
        tracked.Status = session.Status;
        tracked.CompletedAt = session.CompletedAt;
        tracked.ExpiresAt = session.ExpiresAt;
        tracked.Checksum = session.Checksum;
        tracked.ContentFingerprint = session.ContentFingerprint;
        tracked.ContentType = session.ContentType;
        tracked.ClientIp = session.ClientIp;
        tracked.ReceivedChunksCsv = session.ReceivedChunksCsv;
        tracked.Version = session.Version + 1;

        await _db.SaveChangesAsync(ct);
        session.Version = tracked.Version;
    }

    public async Task DeleteAsync(UploadSession session, CancellationToken ct = default)
    {
        var tracked = await _db.UploadSessions.FirstOrDefaultAsync(x => x.Id == session.Id, ct);
        if (tracked is null)
            return;

        _db.UploadSessions.Remove(tracked);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<UploadSession>> GetExpiredPendingAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.UploadSessions.AsNoTracking()
            .Where(x => (x.Status == UploadStatus.Pending || x.Status == UploadStatus.Completing)
                        && x.ExpiresAt <= now)
            .ToListAsync(ct);
    }

    public Task<UploadSession?> FindCompletedByContentAsync(
        string checksumSha256Hex,
        long totalSize,
        CancellationToken ct = default)
    {
        var hash = checksumSha256Hex.Trim().ToLowerInvariant();
        if (hash.StartsWith("sha256:"))
            hash = hash["sha256:".Length..];

        return _db.UploadSessions.AsNoTracking()
            .Where(x => x.Status == UploadStatus.Completed
                        && x.TotalSize == totalSize
                        && x.Checksum == hash
                        && x.FinalFileName != null)
            .OrderByDescending(x => x.CompletedAt)
            .FirstOrDefaultAsync(ct);
    }

    public Task<UploadSession?> FindCompletedByFingerprintAsync(
        string contentFingerprintHex,
        long totalSize,
        CancellationToken ct = default)
    {
        var fp = contentFingerprintHex.Trim().ToLowerInvariant();
        if (fp.StartsWith("fp:"))
            fp = fp[3..];
        else if (fp.StartsWith("sample:"))
            fp = fp[7..];

        return _db.UploadSessions.AsNoTracking()
            .Where(x => x.Status == UploadStatus.Completed
                        && x.TotalSize == totalSize
                        && x.ContentFingerprint == fp
                        && x.FinalFileName != null)
            .OrderByDescending(x => x.CompletedAt)
            .FirstOrDefaultAsync(ct);
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
            .Where(x => (x.Status == UploadStatus.Pending || x.Status == UploadStatus.Completing)
                        && x.ExpiresAt > now)
            .SumAsync(x => (long?)x.TotalSize, ct) ?? 0L;
    }

    public async Task<long> SumActivePendingBytesByIpAsync(string clientIp, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.UploadSessions
            .Where(x => (x.Status == UploadStatus.Pending || x.Status == UploadStatus.Completing)
                        && x.ExpiresAt > now
                        && x.ClientIp == clientIp)
            .SumAsync(x => (long?)x.TotalSize, ct) ?? 0L;
    }

    public async Task<bool> TryBeginCompleteAsync(Guid id, CancellationToken ct = default)
    {
        var rows = await _db.UploadSessions
            .Where(x => x.Id == id && x.Status == UploadStatus.Pending)
            .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, UploadStatus.Completing)
                    .SetProperty(x => x.Version, x => x.Version + 1),
                ct);

        return rows == 1;
    }

    public async Task<bool> TryFinishCompleteAsync(
        Guid id,
        string finalFileName,
        string? checksum,
        string? contentFingerprint = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var rows = await _db.UploadSessions
            .Where(x => x.Id == id && x.Status == UploadStatus.Completing)
            .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, UploadStatus.Completed)
                    .SetProperty(x => x.FinalFileName, finalFileName)
                    .SetProperty(x => x.Checksum, checksum)
                    .SetProperty(x => x.ContentFingerprint, contentFingerprint)
                    .SetProperty(x => x.CompletedAt, now)
                    .SetProperty(x => x.Version, x => x.Version + 1),
                ct);

        return rows == 1;
    }

    public async Task<bool> TryFailCompleteAsync(Guid id, string? checksum, CancellationToken ct = default)
    {
        var rows = await _db.UploadSessions
            .Where(x => x.Id == id && x.Status == UploadStatus.Completing)
            .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, UploadStatus.Failed)
                    .SetProperty(x => x.Checksum, checksum)
                    .SetProperty(x => x.Version, x => x.Version + 1),
                ct);

        return rows == 1;
    }

    public async Task<bool> TryClaimExpiredAsync(Guid id, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var rows = await _db.UploadSessions
            .Where(x => x.Id == id
                        && (x.Status == UploadStatus.Pending || x.Status == UploadStatus.Completing)
                        && x.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, UploadStatus.Expired)
                    .SetProperty(x => x.Version, x => x.Version + 1),
                ct);

        return rows == 1;
    }

    public async Task<bool> TryAbortAsync(Guid id, CancellationToken ct = default)
    {
        var rows = await _db.UploadSessions
            .Where(x => x.Id == id && x.Status == UploadStatus.Pending)
            .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, UploadStatus.Aborted)
                    .SetProperty(x => x.Version, x => x.Version + 1),
                ct);

        return rows == 1;
    }
}
