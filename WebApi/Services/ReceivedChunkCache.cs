using System.Collections.Concurrent;
using WebApi.Interfaces;

namespace WebApi.Services;

/// <summary>
/// Singleton ConcurrentDictionary-backed received-chunk tracker.
/// Must be singleton so parallel PUT requests share the same map.
/// </summary>
public sealed class ReceivedChunkCache : IReceivedChunkCache
{
    private readonly ConcurrentDictionary&lt;Guid, ConcurrentDictionary&lt;int, byte&gt;&gt; _maps = new();

    public ConcurrentDictionary&lt;int, byte&gt; GetOrCreate(Guid uploadId) =&gt;
        _maps.GetOrAdd(uploadId, static _ =&gt; new ConcurrentDictionary&lt;int, byte&gt;());

    public bool TryGet(Guid uploadId, out ConcurrentDictionary&lt;int, byte&gt; map) =&gt;
        _maps.TryGetValue(uploadId, out map!);

    public void Remove(Guid uploadId) =&gt;
        _maps.TryRemove(uploadId, out _);
}
