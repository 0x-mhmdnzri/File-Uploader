using System.Collections.Concurrent;
using WebApi.Domain;
using WebApi.Interfaces;

namespace WebApi.Repositories;

public class InMemoryUploadRepository : IUploadRepository
{
    private readonly ConcurrentDictionary<Guid, UploadSession> _store = new();
    private readonly object _gate = new();

    public Task AddAsync(UploadSession session, CancellationToken cancellationToken = default)
    {
        session.Version = 0;
        _store[session.Id] = Clone(session);
        return Task.CompletedTask;
    }

    public Task<UploadSession?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!_store.TryGetValue(id, out var session))
            return Task.FromResult<UploadSession?>(null);

        return Task.FromResult<UploadSession?>(Clone(session));
    }

    public Task UpdateAsync(UploadSession session, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_store.TryGetValue(session.Id, out var current))
                throw new InvalidOperationException($"Session {session.Id} not found");

            if (current.Version != session.Version)
                throw new InvalidOperationException("Session version conflict.");

            var next = Clone(session);
            next.Version = current.Version + 1;
            _store[session.Id] = next;
            session.Version = next.Version;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(UploadSession session, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(session.Id, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UploadSession>> GetExpiredPendingAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var expired = _store.Values
            .Where(s => (s.Status == UploadStatus.Pending || s.Status == UploadStatus.Completing)
                        && s.ExpiresAt <= now)
            .Select(Clone)
            .ToList();

        return Task.FromResult<IReadOnlyList<UploadSession>>(expired);
    }

    public Task<int> CountActivePendingByIpAsync(string clientIp, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var count = _store.Values.Count(s =>
            s.Status == UploadStatus.Pending &&
            s.ExpiresAt > now &&
            string.Equals(s.ClientIp, clientIp, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(count);
    }

    public Task<long> SumCompletedBytesAsync(CancellationToken cancellationToken = default)
    {
        var sum = _store.Values
            .Where(s => s.Status == UploadStatus.Completed)
            .Sum(s => s.TotalSize);

        return Task.FromResult(sum);
    }

    public Task<long> SumCompletedBytesByIpAsync(string clientIp, CancellationToken cancellationToken = default)
    {
        var sum = _store.Values
            .Where(s => s.Status == UploadStatus.Completed &&
                        string.Equals(s.ClientIp, clientIp, StringComparison.OrdinalIgnoreCase))
            .Sum(s => s.TotalSize);

        return Task.FromResult(sum);
    }

    public Task<long> SumActivePendingBytesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var sum = _store.Values
            .Where(s => (s.Status == UploadStatus.Pending || s.Status == UploadStatus.Completing)
                        && s.ExpiresAt > now)
            .Sum(s => s.TotalSize);

        return Task.FromResult(sum);
    }

    public Task<long> SumActivePendingBytesByIpAsync(string clientIp, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var sum = _store.Values
            .Where(s => (s.Status == UploadStatus.Pending || s.Status == UploadStatus.Completing)
                        && s.ExpiresAt > now &&
                        string.Equals(s.ClientIp, clientIp, StringComparison.OrdinalIgnoreCase))
            .Sum(s => s.TotalSize);

        return Task.FromResult(sum);
    }

    public Task<bool> TryBeginCompleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_store.TryGetValue(id, out var current))
                return Task.FromResult(false);

            if (current.Status != UploadStatus.Pending)
                return Task.FromResult(false);

            var next = Clone(current);
            next.Status = UploadStatus.Completing;
            next.Version = current.Version + 1;
            _store[id] = next;
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryFinishCompleteAsync(
        Guid id,
        string finalFileName,
        string? checksum,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_store.TryGetValue(id, out var current))
                return Task.FromResult(false);

            if (current.Status != UploadStatus.Completing)
                return Task.FromResult(false);

            var next = Clone(current);
            next.Status = UploadStatus.Completed;
            next.FinalFileName = finalFileName;
            next.Checksum = checksum;
            next.CompletedAt = DateTime.UtcNow;
            next.Version = current.Version + 1;
            _store[id] = next;
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryFailCompleteAsync(Guid id, string? checksum, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_store.TryGetValue(id, out var current))
                return Task.FromResult(false);

            if (current.Status != UploadStatus.Completing)
                return Task.FromResult(false);

            var next = Clone(current);
            next.Status = UploadStatus.Failed;
            next.Checksum = checksum ?? next.Checksum;
            next.Version = current.Version + 1;
            _store[id] = next;
            return Task.FromResult(true);
        }
    }

    private static UploadSession Clone(UploadSession s) => new()
    {
        Id = s.Id,
        FileName = s.FileName,
        FinalFileName = s.FinalFileName,
        TotalSize = s.TotalSize,
        ChunkSize = s.ChunkSize,
        TotalChunks = s.TotalChunks,
        Status = s.Status,
        Version = s.Version,
        CreatedAt = s.CreatedAt,
        CompletedAt = s.CompletedAt,
        ExpiresAt = s.ExpiresAt,
        Checksum = s.Checksum,
        ContentType = s.ContentType,
        ClientIp = s.ClientIp,
        ReceivedChunksCsv = s.ReceivedChunksCsv
    };
}
