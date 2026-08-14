using System.Collections.Concurrent;
using WebApi.Interfaces;

namespace WebApi.Services;

/// <summary>
/// Singleton ConcurrentDictionary-backed received-chunk tracker.
/// Must be singleton so parallel PUT requests share the same map.
/// </summary>
public sealed class ReceivedChunkCache : IReceivedChunkCache
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<int, byte>> _maps = new();

    public ConcurrentDictionary<int, byte> GetOrCreate(Guid uploadId) =>
        _maps.GetOrAdd(uploadId, static _ => new ConcurrentDictionary<int, byte>());

    public bool TryGet(Guid uploadId, out ConcurrentDictionary<int, byte> map) =>
        _maps.TryGetValue(uploadId, out map!);

    public void Remove(Guid uploadId) =>
        _maps.TryRemove(uploadId, out _);
}
