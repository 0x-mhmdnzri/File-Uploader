using System.Collections.Concurrent;

namespace WebApi.Interfaces;

/// <summary>
/// Process-wide (or Redis-shared) cache of received chunk indexes with sliding TTL
/// and soft size limit (perf 2.3 / 4.4). Disk remains the source of truth at complete.
/// </summary>
public interface IReceivedChunkCache
{
    ConcurrentDictionary<int, byte> GetOrCreate(Guid uploadId);

    bool TryGet(Guid uploadId, out ConcurrentDictionary<int, byte> map);

    /// <summary>Mark a chunk as received (updates local map and any shared store).</summary>
    void Mark(Guid uploadId, int chunkIndex);

    void Remove(Guid uploadId);
}
