using System.Collections.Concurrent;

namespace WebApi.Interfaces;

/// <summary>
/// Process-wide lock-free cache of received chunk indexes with sliding TTL
/// and soft size limit (perf 2.3). Disk remains the source of truth at complete;
/// this accelerates status/UI and idempotent PUT checks only.
/// </summary>
public interface IReceivedChunkCache
{
    ConcurrentDictionary<int, byte> GetOrCreate(Guid uploadId);

    bool TryGet(Guid uploadId, out ConcurrentDictionary<int, byte> map);

    void Remove(Guid uploadId);
}
