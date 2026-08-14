using WebApi.Domain;
using WebApi.Events;
using WebApi.Interfaces;
using WebApi.Storages;

namespace WebApi.Services;

public class UploadService : IUploadService
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
                throw new InvalidOperationException($"Rate limit exceeded: maximum {MaxPendingUploadsPerIp} concurrent pending uploads per IP.");
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
            TempFolder = await _storage.GetTempFolderAsync(id)
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

        s.ReceivedChunks[chunkIndex] = true;
        await _repo.UpdateAsync(s);
        await _eventBus.PublishAsync(new UploadEvent(uploadId, "ChunkReceived", s.ClientIp, new { ChunkIndex = chunkIndex }));
    }

    public async Task MergeChunksAsync(Guid uploadId)
    {
        var s = await _repo.GetAsync(uploadId)
            ?? throw new InvalidOperationException("Upload session not found");

        if (s.Status != UploadStatus.Pending)
            throw new InvalidOperationException($"Cannot complete. Session status is {s.Status}");

        await using var ms = new MemoryStream();
        await _storage.MergeAsync(uploadId, s.FileName, s.TotalChunks, ms, CancellationToken.None);

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
