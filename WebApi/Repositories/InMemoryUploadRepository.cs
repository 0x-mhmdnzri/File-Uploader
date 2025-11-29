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
    public Task<UploadSession> GetAsync(Guid id)
    {
        _store.TryGetValue(id, out var s);
        return Task.FromResult(s);
    }
    public Task UpdateAsync(UploadSession session)
    {
        _store[session.Id] = session;
        return Task.CompletedTask;
    }
}