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

    public Task<int> CountActivePendingByIpAsync(string ip, CancellationToken cancellationToken = default)
    {
        var count = _store.Values.Count(s =>
            s.Status == UploadStatus.Pending &&
            string.Equals(s.ClientIp, ip, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(count);
    }
}