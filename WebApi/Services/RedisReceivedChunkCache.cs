using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using WebApi.Interfaces;
using WebApi.Options;

namespace WebApi.Services;

/// <summary>
/// Redis SET of received chunk indexes, shared across nodes (perf 4.4).
/// </summary>
public sealed class RedisReceivedChunkCache : IReceivedChunkCache
{
    private readonly IDatabase _db;
    private readonly string _prefix;
    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<int, byte>> _local = new();

    public RedisReceivedChunkCache(IConnectionMultiplexer mux, IOptions<RedisOptions> options)
    {
        _db = mux.GetDatabase();
        var o = options.Value;
        _prefix = o.KeyPrefix ?? "fu:";
        _ttl = TimeSpan.FromSeconds(Math.Max(30, o.ReceivedChunksTtlSeconds));
    }

    private string Key(Guid id) => $"{_prefix}chunks:{id:N}";

    public ConcurrentDictionary<int, byte> GetOrCreate(Guid uploadId)
    {
        return _local.GetOrAdd(uploadId, id =>
        {
            var map = new ConcurrentDictionary<int, byte>();
            try
            {
                foreach (var m in _db.SetMembers(Key(id)))
                {
                    if (m.TryParse(out int idx))
                        map.TryAdd(idx, 0);
                }
            }
            catch
            {
                // Redis down — local only
            }
            return map;
        });
    }

    public bool TryGet(Guid uploadId, out ConcurrentDictionary<int, byte> map)
    {
        map = GetOrCreate(uploadId);
        return true;
    }

    public void Mark(Guid uploadId, int chunkIndex)
    {
        GetOrCreate(uploadId).TryAdd(chunkIndex, 0);
        try
        {
            var key = Key(uploadId);
            _db.SetAdd(key, chunkIndex);
            _db.KeyExpire(key, _ttl);
        }
        catch
        {
            // best-effort shared write
        }
    }

    public void Remove(Guid uploadId)
    {
        _local.TryRemove(uploadId, out _);
        try { _db.KeyDelete(Key(uploadId)); } catch { /* ignore */ }
    }
}
