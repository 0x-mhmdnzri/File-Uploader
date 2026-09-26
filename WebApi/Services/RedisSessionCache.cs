using System.Text.Json;
using StackExchange.Redis;
using WebApi.Domain;
using WebApi.Interfaces;
using WebApi.Options;
using Microsoft.Extensions.Options;

namespace WebApi.Services;

/// <summary>
/// Redis-backed session cache shared across API nodes (perf 4.4).
/// </summary>
public sealed class RedisSessionCache : ISessionCache
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDatabase _db;
    private readonly string _prefix;
    private readonly TimeSpan _ttl;

    public RedisSessionCache(IConnectionMultiplexer mux, IOptions<RedisOptions> options)
    {
        _db = mux.GetDatabase();
        var o = options.Value;
        _prefix = o.KeyPrefix ?? "fu:";
        _ttl = TimeSpan.FromSeconds(Math.Max(5, o.SessionTtlSeconds));
    }

    private string Key(Guid id) => $"{_prefix}session:{id:N}";

    public bool TryGet(Guid uploadId, out UploadSession session)
    {
        session = null!;
        var val = _db.StringGet(Key(uploadId));
        if (val.IsNullOrEmpty) return false;
        try
        {
            session = JsonSerializer.Deserialize<UploadSession>((string)val!, JsonOpts)!;
            return session is not null;
        }
        catch
        {
            return false;
        }
    }

    public void Set(UploadSession session)
    {
        var json = JsonSerializer.Serialize(session, JsonOpts);
        _db.StringSet(Key(session.Id), json, _ttl);
    }

    public void Remove(Guid uploadId) => _db.KeyDelete(Key(uploadId));
}
