using Microsoft.Extensions.Options;
using WebApi.Domain;
using WebApi.Interfaces;
using WebApi.Storages;

namespace WebApi.Services;

public class UploadService : IUploadService
{
    private readonly IUploadRepository _repo;
    private readonly IFileStorage _storage;
    private readonly StorageOptions _options;
    private readonly ILogger<UploadService> _logger;

    public UploadService(
        IUploadRepository repo,
        IFileStorage storage,
        IOptions<StorageOptions> options,
        ILogger<UploadService> logger)
    {
        _repo = repo;
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<UploadSession> InitiateAsync(
        string fileName,
        long totalSize,
        int chunkSize,
        string? contentType = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("fileName is required", nameof(fileName));

        if (totalSize <= 0)
            throw new ArgumentException("totalSize must be positive", nameof(totalSize));

        if (chunkSize <= 0)
            throw new ArgumentException("chunkSize must be positive", nameof(chunkSize));

        await _storage.EnsureDirectoriesAsync(ct);

        var session = new UploadSession
        {
            Id = Guid.NewGuid(),
            FileName = Path.GetFileName(fileName),
            TotalSize = totalSize,
            ChunkSize = chunkSize,
            TotalChunks = (int)Math.Ceiling((double)totalSize / chunkSize),
            Status = UploadStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(_options.PendingTtlHours),
            ContentType = contentType
        };

        await _repo.AddAsync(session, ct);

        _logger.LogInformation(
            "Upload initiated {UploadId} for {FileName} ({TotalSize} bytes, {TotalChunks} chunks)",
            session.Id, session.FileName, session.TotalSize, session.TotalChunks);

        return session;
    }

    public async Task MarkChunkReceivedAsync(Guid uploadId, int chunkIndex, CancellationToken ct = default)
    {
        var session = await _repo.GetAsync(uploadId, ct)
                     ?? throw new InvalidOperationException($"Upload session {uploadId} not found");

        if (session.Status != UploadStatus.Pending)
            throw new InvalidOperationException($"Cannot accept chunks for session in status {session.Status}");

        if (session.IsExpired())
            throw new InvalidOperationException($"Upload session {uploadId} has expired");

        session.MarkChunkReceived(chunkIndex);
        await _repo.UpdateAsync(session, ct);
    }

    public async Task<string> CompleteAsync(Guid uploadId, CancellationToken ct = default)
    {
        var session = await _repo.GetAsync(uploadId, ct)
                     ?? throw new InvalidOperationException($"Upload session {uploadId} not found");

        if (session.Status == UploadStatus.Completed)
            return session.FinalFileName ?? session.FileName;

        if (session.Status != UploadStatus.Pending)
            throw new InvalidOperationException($"Cannot complete session in status {session.Status}");

        if (session.IsExpired())
            throw new InvalidOperationException($"Upload session {uploadId} has expired");

        var received = session.GetReceivedChunks();
        if (received.Count != session.TotalChunks)
        {
            throw new InvalidOperationException(
                $"Not all chunks received. Expected {session.TotalChunks}, got {received.Count}");
        }

        try
        {
            var finalPath = await _storage.MergeAsync(uploadId, session.FileName, session.TotalChunks, ct);

            session.Status = UploadStatus.Completed;
            session.CompletedAt = DateTime.UtcNow;
            session.FinalFileName = Path.GetFileName(finalPath);
            await _repo.UpdateAsync(session, ct);

            _logger.LogInformation(
                "Upload completed {UploadId} → {FinalFileName}",
                session.Id, session.FinalFileName);

            return finalPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Merge failed for upload {UploadId}", uploadId);
            session.Status = UploadStatus.Failed;
            await _repo.UpdateAsync(session, ct);
            throw;
        }
    }

    public async Task AbortAsync(Guid uploadId, CancellationToken ct = default)
    {
        var session = await _repo.GetAsync(uploadId, ct)
                     ?? throw new InvalidOperationException($"Upload session {uploadId} not found");

        if (session.Status is UploadStatus.Completed or UploadStatus.Aborted)
            return;

        session.Status = UploadStatus.Aborted;
        await _repo.UpdateAsync(session, ct);
        await _storage.DeleteTempFolderAsync(uploadId, ct);

        _logger.LogInformation("Upload aborted {UploadId}", uploadId);
    }

    public Task<UploadSession?> GetStatusAsync(Guid uploadId, CancellationToken ct = default)
    {
        return _repo.GetAsync(uploadId, ct);
    }
}
