using System.Collections.Concurrent;
using WebApi.Domain;
using WebApi.Interfaces;

namespace WebApi.Repositories;

public class InMemoryUploadRepository : IUploadRepository
{
    private readonly ConcurrentDictionary<Guid, UploadSession> _store = new();

    public Task AddAsync(UploadSession session)
    {
        _store[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task<UploadSession?> GetAsync(Guid id)
    {
        _store.TryGetValue(id, out var session);
        return Task.FromResult(session);
    }

    public Task UpdateAsync(UploadSession session)
    {
        _store[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id)
    {
        _store.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UploadSession>> GetExpiredPendingAsync(DateTime utcNow)
    {
        var expired = _store.Values
            .Where(s => s.Status == UploadStatus.Pending && s.ExpiresAt <= utcNow)
            .ToList();

        return Task.FromResult<IReadOnlyList<UploadSession>>(expired);
    }

    public Task<int> CountActivePendingByIpAsync(string clientIp)
    {
        if (string.IsNullOrWhiteSpace(clientIp))
            return Task.FromResult(0);

        var count = _store.Values.Count(s =>
            s.Status == UploadStatus.Pending &&
            string.Equals(s.ClientIp, clientIp, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(count);
    }
}
