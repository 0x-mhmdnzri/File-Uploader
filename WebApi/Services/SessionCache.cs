using System.Collections.Concurrent;
using WebApi.Domain;
using WebApi.Interfaces;

namespace WebApi.Services;

/// <summary>
/// ConcurrentDictionary-backed session cache with absolute TTL per entry.
/// </summary>
public sealed class SessionCache : ISessionCache
{
    private readonly ConcurrentDictionary<Guid, Entry> _map = new();
    private readonly TimeSpan _ttl;

    public SessionCache()
        : this(TimeSpan.FromSeconds(30))
    {
    }

    public SessionCache(TimeSpan ttl)
    {
        _ttl = ttl <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : ttl;
    }

    public bool TryGet(Guid uploadId, out UploadSession session)
    {
        session = null!;
        if (!_map.TryGetValue(uploadId, out var entry))
            return false;

        if (DateTime.UtcNow >= entry.ExpiresAt)
        {
            _map.TryRemove(uploadId, out _);
            return false;
        }

        session = entry.Session;
        return true;
    }

    public void Set(UploadSession session)
    {
        _map[session.Id] = new Entry(session, DateTime.UtcNow.Add(_ttl));
    }

    public void Remove(Guid uploadId) => _map.TryRemove(uploadId, out _);

    private sealed record Entry(UploadSession Session, DateTime ExpiresAt);
}
