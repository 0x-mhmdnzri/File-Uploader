using System.Collections.Concurrent;
using WebApi.Domain;
using WebApi.Interfaces;

namespace WebApi.Repositories;
 

public class InMemoryUploadRepository : IUploadRepository
{
    private readonly ConcurrentDictionary<Guid, UploadSession> _store = new();

    public Task AddAsync(UploadSession session, CancellationToken cancellationToken = default)
    {
        _store[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task<UploadSession?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(id, out var session);
        return Task.FromResult(session);
    }

    public Task UpdateAsync(UploadSession session, CancellationToken cancellationToken = default)
    {
        _store[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(UploadSession session, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(session.Id, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UploadSession>> GetExpiredPendingAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var expired = _store.Values
            .Where(s => s.Status == UploadStatus.Pending && s.ExpiresAt <= now)
            .ToList();

        return Task.FromResult<IReadOnlyList<UploadSession>>(expired);
    }

    public Task<int> CountActivePendingByIpAsync(string clientIp, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var count = _store.Values.Count(s =>
            s.Status == UploadStatus.Pending &&
            s.ExpiresAt > now &&
            string.Equals(s.ClientIp, clientIp, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(count);
    }

    public Task<long> SumCompletedBytesAsync(CancellationToken cancellationToken = default)
    {
        var sum = _store.Values
            .Where(s => s.Status == UploadStatus.Completed)
            .Sum(s => s.TotalSize);

        return Task.FromResult(sum);
    }

    public Task<long> SumCompletedBytesByIpAsync(string clientIp, CancellationToken cancellationToken = default)
    {
        var sum = _store.Values
            .Where(s => s.Status == UploadStatus.Completed &&
                        string.Equals(s.ClientIp, clientIp, StringComparison.OrdinalIgnoreCase))
            .Sum(s => s.TotalSize);

        return Task.FromResult(sum);
    }

    public Task<long> SumActivePendingBytesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var sum = _store.Values
            .Where(s => s.Status == UploadStatus.Pending && s.ExpiresAt > now)
            .Sum(s => s.TotalSize);

        return Task.FromResult(sum);
    }

    public Task<long> SumActivePendingBytesByIpAsync(string clientIp, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var sum = _store.Values
            .Where(s => s.Status == UploadStatus.Pending &&
                        s.ExpiresAt > now &&
                        string.Equals(s.ClientIp, clientIp, StringComparison.OrdinalIgnoreCase))
            .Sum(s => s.TotalSize);

        return Task.FromResult(sum);
    }
}