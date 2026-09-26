using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using WebApi.Interfaces;
using WebApi.Storages;

namespace WebApi.Services;

/// <summary>
/// Singleton ConcurrentDictionary-backed received-chunk tracker with per-entry TTL
/// (perf 2.3). Disk remains the source of truth at complete; this accelerates
/// status/UI and idempotent PUT checks only.
/// </summary>
public sealed class ReceivedChunkCache : IReceivedChunkCache
{
    private readonly ConcurrentDictionary<Guid, Entry> _maps = new();
    private readonly TimeSpan _ttl;
    private readonly int _maxEntries;

    public ReceivedChunkCache(IOptions<StorageOptions>? options = null)
    {
        var ttlSeconds = options?.Value?.SessionCacheTtlSeconds ?? 30;
        // Received-chunk maps are useful for the whole upload lifetime; keep them
        // a bit longer than the session cache (default 10 minutes).
        _ttl = TimeSpan.FromMinutes(Math.Clamp(ttlSeconds * 20, 5, 60));
        _maxEntries = 10_000; // soft cap to avoid unbounded growth under abuse
    }

    public ConcurrentDictionary<int, byte> GetOrCreate(Guid uploadId)
    {
        EvictIfNeeded();
        var entry = _maps.AddOrUpdate(
            uploadId,
            static id => new Entry(new ConcurrentDictionary<int, byte>(), DateTime.UtcNow),
            static (_, existing) =>
            {
                existing.Touch();
                return existing;
            });
        return entry.Map;
    }

    public bool TryGet(Guid uploadId, out ConcurrentDictionary<int, byte> map)
    {
        map = null!;
        if (!_maps.TryGetValue(uploadId, out var entry))
            return false;

        if (entry.IsExpired(_ttl))
        {
            _maps.TryRemove(uploadId, out _);
            return false;
        }

        entry.Touch(); // sliding expiration
        map = entry.Map;
        return true;
    }

    public void Mark(Guid uploadId, int chunkIndex)
    {
        GetOrCreate(uploadId).TryAdd(chunkIndex, 0);
    }

    public void Remove(Guid uploadId) => _maps.TryRemove(uploadId, out _);

    private void EvictIfNeeded()
    {
        if (_maps.Count < _maxEntries)
            return;

        // Opportunistic eviction of expired entries
        foreach (var kv in _maps)
        {
            if (kv.Value.IsExpired(_ttl))
                _maps.TryRemove(kv.Key, out _);
        }

        // If still over soft cap, drop oldest half (best-effort)
        if (_maps.Count >= _maxEntries)
        {
            var oldest = _maps
                .OrderBy(kv => kv.Value.LastAccessUtc)
                .Take(_maps.Count / 2)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var id in oldest)
                _maps.TryRemove(id, out _);
        }
    }

    private sealed class Entry
    {
        public ConcurrentDictionary<int, byte> Map { get; }
        public DateTime LastAccessUtc { get; private set; }

        public Entry(ConcurrentDictionary<int, byte> map, DateTime now)
        {
            Map = map;
            LastAccessUtc = now;
        }

        public void Touch() => LastAccessUtc = DateTime.UtcNow;

        public bool IsExpired(TimeSpan ttl) => DateTime.UtcNow - LastAccessUtc > ttl;
    }
}
