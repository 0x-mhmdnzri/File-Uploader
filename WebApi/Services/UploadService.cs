using System.Collections.Concurrent;
using WebApi.Domain;
using WebApi.Events;
using WebApi.Interfaces;

namespace WebApi.Services;

public sealed class UploadService : IUploadService
{
    private readonly IUploadRepository _repo;
    private readonly IFileStorage _storage;
    private readonly IUploadEventBus _eventBus;

    private const int MaxPendingUploadsPerIp = 5;
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(2);

    public UploadService(IUploadRepository repo, IFileStorage storage, IUploadEventBus eventBus)
    {
        _repo = repo;
        _storage = storage;
        _eventBus = eventBus;
    }

    public async Task<UploadSession> InitiateAsync(string fileName, long totalSize, int chunkSize, string? clientIp = null)
    {
        if (!string.IsNullOrWhiteSpace(clientIp))
        {
            var activeCount = await _repo.CountActivePendingByIpAsync(clientIp);
            if (activeCount >= MaxPendingUploadsPerIp)
            {
                throw new InvalidOperationException(
                    $"Rate limit exceeded: maximum {MaxPendingUploadsPerIp} concurrent pending uploads per IP.");
            }
        }

        await _storage.EnsureDirectoriesAsync();

        var id = Guid.NewGuid();
        var session = new UploadSession
        {
            Id = id,
            FileName = Path.GetFileName(fileName),
            TotalSize = totalSize,
            ChunkSize = chunkSize,
            TotalChunks = (int)Math.Ceiling((double)totalSize / chunkSize),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(SessionLifetime),
            Status = UploadStatus.Pending,
            ClientIp = clientIp,
            TempFolder = await _storage.GetTempFolderAsync(id),
            ReceivedChunks = new ConcurrentDictionary<int, bool>()
        };

        await _repo.AddAsync(session);
        await _eventBus.PublishAsync(new UploadEvent(session.Id, "Initiated", session.ClientIp));

        return session;
    }

    public async Task MarkChunkReceivedAsync(Guid uploadId, int chunkIndex)
    {
        var s = await _repo.GetAsync(uploadId)
            ?? throw new InvalidOperationException("Upload session not found");

        if (s.Status != UploadStatus.Pending)
            throw new InvalidOperationException($"Cannot upload chunk. Session status is {s.Status}");

        if (s.ExpiresAt <= DateTime.UtcNow)
        {
            s.Status = UploadStatus.Expired;
            await _repo.UpdateAsync(s);
            throw new InvalidOperationException("Upload session has expired");
        }

        // ConcurrentDictionary is already thread-safe.
        // We only need to persist the change; no coarse lock required.
        s.ReceivedChunks[chunkIndex] = true;

        // Optional: we could use Interlocked to track received count, but dictionary Count is fine for now.
        await _repo.UpdateAsync(s);

        // Fire-and-forget style event (Channel is already concurrent)
        _ = _eventBus.PublishAsync(new UploadEvent(uploadId, "ChunkReceived", s.ClientIp, new { ChunkIndex = chunkIndex }));
    }

    public async Task MergeChunksAsync(Guid uploadId)
    {
        var s = await _repo.GetAsync(uploadId)
            ?? throw new InvalidOperationException("Upload session not found");

        if (s.Status != UploadStatus.Pending)
            throw new InvalidOperationException($"Cannot complete. Session status is {s.Status}");

        // ---- Strong end-to-end verification using ConcurrentBag ----
        var missing = new ConcurrentBag();

        // Parallel check of dictionary + physical files
        await Parallel.ForEachAsync(
            Enumerable.Range(0, s.TotalChunks),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 },
            async (i, ct) =>
            {
                if (!s.ReceivedChunks.ContainsKey(i) || !s.ReceivedChunks[i])
                {
                    missing.Add(i);
                    return;
                }

                // Double-check physical existence (defense in depth)
                if (!await _storage.ChunkExistsAsync(uploadId, i))
                {
                    missing.Add(i);
                }
            });

        if (!missing.IsEmpty)
        {
            var sample = string.Join(", ", missing.OrderBy(x => x).Take(40));
            throw new InvalidOperationException(
                $"Cannot complete: {missing.Count}/{s.TotalChunks} chunks missing or not marked. Sample: {sample}");
        }

        // Merge (already parallel + verified inside storage)
        await _storage.MergeAsync(uploadId, s.FileName, s.TotalChunks, Stream.Null, CancellationToken.None);

        s.Completed = true;
        s.Status = UploadStatus.Completed;
        await _repo.UpdateAsync(s);

        await _eventBus.PublishAsync(new UploadEvent(uploadId, "Completed", s.ClientIp));
    }

    public Task<UploadSession?> GetStatusAsync(Guid uploadId)
    {
        return _repo.GetAsync(uploadId);
    }
}
