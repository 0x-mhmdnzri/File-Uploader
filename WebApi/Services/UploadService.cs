using Microsoft.Extensions.Options;
using WebApi.Domain;
using WebApi.Domain.Events;
using WebApi.Interfaces;
using WebApi.Metrics;
using WebApi.Storages;

namespace WebApi.Services;

public class UploadService : IUploadService
{
    private readonly IUploadRepository _repo;
    private readonly IFileStorage _storage;
    private readonly IUploadEventPublisher _events;
    private readonly StorageOptions _options;
    private readonly IUploadMetrics _metrics;
    private readonly ILogger<UploadService> _logger;

    public UploadService(
        IUploadRepository repo,
        IFileStorage storage,
        IUploadEventPublisher events,
        IOptions<StorageOptions> options,
        IUploadMetrics metrics,
        ILogger<UploadService> logger)
    {
        _repo = repo;
        _storage = storage;
        _events = events;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<UploadSession> InitiateAsync(
        string fileName,
        long totalSize,
        int chunkSize,
        string? contentType = null,
        string? checksum = null,
        string? clientIp = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("fileName is required", nameof(fileName));

        if (totalSize <= 0)
            throw new ArgumentException("totalSize must be positive", nameof(totalSize));

        if (chunkSize <= 0)
            throw new ArgumentException("chunkSize must be positive", nameof(chunkSize));

        ValidateFileSize(totalSize);
        ValidateChunkSize(chunkSize);
        ValidateExtension(fileName);

        if (!string.IsNullOrWhiteSpace(clientIp) && _options.MaxPendingSessionsPerIp > 0)
        {
            var pendingCount = await _repo.CountActivePendingByIpAsync(clientIp, ct);
            if (pendingCount >= _options.MaxPendingSessionsPerIp)
            {
                throw new InvalidOperationException(
                    $"Too many pending uploads from this IP. Limit is {_options.MaxPendingSessionsPerIp}.");
            }
        }

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
            ContentType = contentType,
            Checksum = NormalizeChecksum(checksum),
            ClientIp = clientIp
        };

        await _repo.AddAsync(session, ct);
        _metrics.RecordInitiated();

        _logger.LogInformation(
            "Upload initiated {UploadId} for {FileName} ({TotalSize} bytes, {TotalChunks} chunks, ip={ClientIp})",
            session.Id, session.FileName, session.TotalSize, session.TotalChunks, clientIp ?? "-");

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

        // Best-effort update of the CSV for status/resume UI.
        // Under high concurrency this can lose updates; CompleteAsync uses the filesystem as source of truth.
        session.MarkChunkReceived(chunkIndex);
        await _repo.UpdateAsync(session, ct);
        _metrics.RecordChunkUploaded();
    }

    public async Task<string> CompleteAsync(Guid uploadId, string? checksum = null, CancellationToken ct = default)
    {
        var session = await _repo.GetAsync(uploadId, ct)
                     ?? throw new InvalidOperationException($"Upload session {uploadId} not found");

        if (session.Status == UploadStatus.Completed)
            return session.FinalFileName ?? session.FileName;

        if (session.Status != UploadStatus.Pending)
            throw new InvalidOperationException($"Cannot complete session in status {session.Status}");

        if (session.IsExpired())
            throw new InvalidOperationException($"Upload session {uploadId} has expired");

        // Disk is the source of truth. Parallel MarkChunkReceivedAsync calls race on ReceivedChunksCsv
        // and can under-count; the part files themselves are written independently and are reliable.
        var onDisk = await _storage.GetExistingChunkIndexesAsync(uploadId, ct);
        var receivedCount = onDisk.Count;

        if (receivedCount != session.TotalChunks)
        {
            // Also surface which indexes are missing to help debugging / client resume.
            var missing = Enumerable.Range(0, session.TotalChunks)
                .Where(i => !onDisk.Contains(i))
                .Take(20)
                .ToArray();

            throw new InvalidOperationException(
                $"Not all chunks received. Expected {session.TotalChunks}, got {receivedCount} on disk. " +
                $"Missing (sample): [{string.Join(", ", missing)}]");
        }

        // Keep CSV in sync for status endpoint consumers.
        session.ReceivedChunksCsv = string.Join(',', onDisk.OrderBy(x => x));

        var expectedChecksum = NormalizeChecksum(checksum) ?? session.Checksum;

        string finalPath;
        try
        {
            finalPath = await _storage.MergeAsync(uploadId, session.FileName, session.TotalChunks, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Merge failed for upload {UploadId}", uploadId);
            session.Status = UploadStatus.Failed;
            await _repo.UpdateAsync(session, ct);
            _metrics.RecordFailed();
            await SafePublishFailedAsync(session, "merge_failed", ct);
            throw;
        }

        if (expectedChecksum is not null)
        {
            string actualChecksum;
            try
            {
                actualChecksum = await _storage.ComputeSha256Async(finalPath, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Checksum computation failed for upload {UploadId}", uploadId);
                await _storage.DeleteFinalFileAsync(Path.GetFileName(finalPath), ct);
                session.Status = UploadStatus.Failed;
                await _repo.UpdateAsync(session, ct);
                _metrics.RecordFailed();
                await SafePublishFailedAsync(session, "checksum_compute_failed", ct);
                throw;
            }

            if (!string.Equals(actualChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Checksum mismatch for upload {UploadId}. Expected={Expected}, Actual={Actual}",
                    uploadId, expectedChecksum, actualChecksum);

                await _storage.DeleteFinalFileAsync(Path.GetFileName(finalPath), ct);
                session.Status = UploadStatus.Failed;
                session.Checksum = actualChecksum;
                await _repo.UpdateAsync(session, ct);
                _metrics.RecordFailed();
                await SafePublishFailedAsync(session, "checksum_mismatch", ct);

                throw new InvalidOperationException(
                    $"Checksum mismatch. Expected {expectedChecksum}, got {actualChecksum}");
            }

            session.Checksum = actualChecksum;
            _logger.LogInformation("Checksum verified for upload {UploadId}", uploadId);
        }
        else
        {
            try
            {
                session.Checksum = await _storage.ComputeSha256Async(finalPath, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Optional checksum computation failed for {UploadId}", uploadId);
            }
        }

        session.Status = UploadStatus.Completed;
        session.CompletedAt = DateTime.UtcNow;
        session.FinalFileName = Path.GetFileName(finalPath);
        await _repo.UpdateAsync(session, ct);
        _metrics.RecordCompleted(session.TotalSize);

        _logger.LogInformation(
            "Upload completed {UploadId} → {FinalFileName}",
            session.Id, session.FinalFileName);

        await SafePublishCompletedAsync(session, ct);

        return finalPath;
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
        _metrics.RecordAborted();

        _logger.LogInformation("Upload aborted {UploadId}", uploadId);

        try
        {
            await _events.PublishAbortedAsync(new UploadAbortedEvent(
                session.Id,
                session.FileName,
                session.ClientIp,
                DateTime.UtcNow), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish UploadAborted for {UploadId}", uploadId);
        }
    }

    public Task<UploadSession?> GetStatusAsync(Guid uploadId, CancellationToken ct = default)
    {
        return _repo.GetAsync(uploadId, ct);
    }

    private async Task SafePublishCompletedAsync(UploadSession session, CancellationToken ct)
    {
        try
        {
            await _events.PublishCompletedAsync(new UploadCompletedEvent(
                session.Id,
                session.FileName,
                session.FinalFileName ?? session.FileName,
                session.TotalSize,
                session.ContentType,
                session.Checksum,
                session.ClientIp,
                session.CompletedAt ?? DateTime.UtcNow), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish UploadCompleted for {UploadId}", session.Id);
        }
    }

    private async Task SafePublishFailedAsync(UploadSession session, string reason, CancellationToken ct)
    {
        try
        {
            await _events.PublishFailedAsync(new UploadFailedEvent(
                session.Id,
                session.FileName,
                reason,
                session.ClientIp,
                DateTime.UtcNow), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish UploadFailed for {UploadId}", session.Id);
        }
    }

    private void ValidateFileSize(long totalSize)
    {
        if (_options.MaxFileSizeBytes > 0 && totalSize > _options.MaxFileSizeBytes)
        {
            throw new ArgumentException(
                $"File size {totalSize} exceeds maximum allowed {_options.MaxFileSizeBytes} bytes.");
        }
    }

    private void ValidateChunkSize(int chunkSize)
    {
        if (_options.MaxChunkSizeBytes > 0 && chunkSize > _options.MaxChunkSizeBytes)
        {
            throw new ArgumentException(
                $"Chunk size {chunkSize} exceeds maximum allowed {_options.MaxChunkSizeBytes} bytes.");
        }
    }

    private void ValidateExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();

        if (string.IsNullOrEmpty(ext))
        {
            if (_options.AllowedExtensions is { Length: > 0 })
                throw new ArgumentException("Files without extension are not allowed.");
            return;
        }

        var blocked = _options.BlockedExtensions ?? [];
        if (blocked.Any(b => string.Equals(b.TrimStart('.'), ext, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"File extension '.{ext}' is blocked.");

        var allowed = _options.AllowedExtensions ?? [];
        if (allowed.Length > 0 &&
            !allowed.Any(a => string.Equals(a.TrimStart('.'), ext, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"File extension '.{ext}' is not in the allowed list.");
    }

    private static string? NormalizeChecksum(string? checksum)
    {
        if (string.IsNullOrWhiteSpace(checksum))
            return null;

        var normalized = checksum.Trim().ToLowerInvariant();

        if (normalized.StartsWith("sha256:"))
            normalized = normalized["sha256:".Length..];

        if (normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
            throw new ArgumentException("checksum must be a 64-character hex SHA-256 string", nameof(checksum));

        return normalized;
    }
}
