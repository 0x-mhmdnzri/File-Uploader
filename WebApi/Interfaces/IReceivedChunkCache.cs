using System.Collections.Concurrent;

namespace WebApi.Interfaces;

/// <summary>
/// Process-wide lock-free cache of received chunk indexes.
/// Disk remains the source of truth at complete; this accelerates status/UI only.
/// </summary>
public interface IReceivedChunkCache
{
    ConcurrentDictionary&lt;int, byte&gt; GetOrCreate(Guid uploadId);

    bool TryGet(Guid uploadId, out ConcurrentDictionary&lt;int, byte&gt; map);

    void Remove(Guid uploadId);
}
