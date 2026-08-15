using Microsoft.Extensions.Options;
using WebApi.Audit;
using WebApi.Domain;
using WebApi.Domain.Events;
using WebApi.Hashing;
using WebApi.Interfaces;
using WebApi.Metrics;
using WebApi.Storages;

namespace WebApi.Services;

public class UploadService : IUploadService
{
    private readonly IUploadRepository _repo;
    private readonly IFileStorage _storage;
    private readonly IUploadEventPublisher _events;
    private readonly IReceivedChunkCache _receivedCache;
    private readonly ISessionCache _sessionCache;
    private readonly IAuditLogger _audit;
    private readonly StorageOptions _options;
    private readonly IUploadMetrics _metrics;
    private readonly ILogger<UploadService> _logger;

    public UploadService(
        IUploadRepository repo,
        IFileStorage storage,
        IUploadEventPublisher events,
        IReceivedChunkCache receivedCache,
        ISessionCache sessionCache,
        IAuditLogger audit,
        IOptions<StorageOptions> options,
        IUploadMetrics metrics,
        ILogger<UploadService> logger)
    {
        _repo = repo;
        _storage = storage;
        _events = events;
        _receivedCache = receivedCache;
        _sessionCache = sessionCache;
        _audit = audit;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<InitiateResult> InitiateAsync(
        string fileName,
        long totalSize,
        int chunkSize,
        string? contentType = null,
        string? checksum = null,
        string? contentFingerprint = null,
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

        var normalizedChecksum = NormalizeChecksum(checksum);
        var normalizedFingerprint = ContentFingerprint.Normalize(contentFingerprint);

        if (_options.DeduplicateByContent)
        {
            UploadSession? existing = null;
            var hitKind = "";

            if (normalizedChecksum is not null)
            {
                existing = await _repo.FindCompletedByContentAsync(normalizedChecksum, totalSize, ct);
                hitKind = "checksum";
            }

            if (existing is null && normalizedFingerprint is not null)
            {
                existing = await _repo.FindCompletedByFingerprintAsync(normalizedFingerprint, totalSize, ct);
                hitKind = "fingerprint";
            }

            if (existing is not null && !string.IsNullOrWhiteSpace(existing.FinalFileName))
            {
                var finalName = existing.FinalFileName!;
                var stillThere = await _storage.FinalObjectExistsAsync(finalName, totalSize, ct);
                if (stillThere)
                {
                    _logger.LogInformation(
                        "Content dedupe hit ({Kind}): checksum={Checksum} fp={Fp} size={Size} → existing uploadId={UploadId} file={File}",
                        hitKind, normalizedChecksum ?? "-", normalizedFingerprint ?? "-", totalSize, existing.Id, finalName);

                    return new InitiateResult
                    {
                        Session = existing,
                        AlreadyExists = true,
                        ExistingPath = finalName
                    };
                }

                _logger.LogWarning(
                    "Dedupe metadata hit but final object missing for {UploadId} ({File}); creating new upload",
                    existing.Id, finalName);
            }
        }

        if (!string.IsNullOrWhiteSpace(clientIp) && _options.MaxPendingSessionsPerIp > 0)
        {
            var pendingCount = await _repo.CountActivePendingByIpAsync(clientIp, ct);
            if (pendingCount >= _options.MaxPendingSessionsPerIp)
            {
                throw new InvalidOperationException(
                    $"Too many pending uploads from this IP. Limit is {_options.MaxPendingSessionsPerIp}.");
            }
        }

        await EnforceQuotaAsync(totalSize, clientIp, ct);

        await _storage.EnsureDirectoriesAsync(ct);

        var session = new UploadSession
        {
            Id = Guid.NewGuid(),
            FileName = Path.GetFileName(fileName),
            TotalSize = totalSize,
            ChunkSize = chunkSize,
            TotalChunks = (int)Math.Ceiling((double)totalSize / chunkSize),
            Status = UploadStatus.Pending,
            Version = 0,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(_options.PendingTtlHours),
            ContentType = contentType,
            Checksum = normalizedChecksum,
            ContentFingerprint = normalizedFingerprint,
            ClientIp = clientIp
        };

        await _repo.AddAsync(session, ct);
        _receivedCache.GetOrCreate(session.Id);
        _sessionCache.Set(session);
        _metrics.RecordInitiated();

        _audit.UploadInitiated(session.Id, session.FileName, session.TotalSize, session.TotalChunks, clientIp);

        _logger.LogInformation(
            "Upload initiated {UploadId} for {FileName} ({TotalSize} bytes, {TotalChunks} chunks, ip={ClientIp})",
            session.Id, session.FileName, session.TotalSize, session.TotalChunks, clientIp ?? "-");

        return new InitiateResult
        {
            Session = session,
            AlreadyExists = false
        };
    }

    public async Task EnsureCanAcceptChunkAsync(Guid uploadId, int chunkIndex, CancellationToken ct = default)
    {
        var session = await GetSessionHotAsync(uploadId, ct)
                     ?? throw new InvalidOperationException($"Upload session {uploadId} not found");

        if (session.Status != UploadStatus.Pending)
            throw new InvalidOperationException($"Cannot accept chunks for session in status {session.Status}");

        if (session.IsExpired())
            throw new InvalidOperationException($"Upload session {uploadId} has expired");

        if (chunkIndex < 0 || chunkIndex >= session.TotalChunks)
            throw new InvalidOperationException($"Chunk index {chunkIndex} out of range [0, {session.TotalChunks}).");
    }

    public async Task MarkChunkReceivedAsync(Guid uploadId, int chunkIndex, CancellationToken ct = default)
    {
        await EnsureCanAcceptChunkAsync(uploadId, chunkIndex, ct);

        var map = _receivedCache.GetOrCreate(uploadId);
        map.TryAdd(chunkIndex, 0);
        _metrics.RecordChunkUploaded();
    }

    public async Task<string> CompleteAsync(Guid uploadId, string? checksum = null, CancellationToken ct = default)
    {
        _sessionCache.Remove(uploadId);

        var session = await _repo.GetAsync(uploadId, ct)
                     ?? throw new InvalidOperationException($"Upload session {uploadId} not found");

        if (session.Status == UploadStatus.Completed)
            return session.FinalFileName ?? session.FileName;

        if (session.Status == UploadStatus.Completing)
        {
            throw new InvalidOperationException(
                "Upload is already being completed on another node (or this node). Retry status shortly.");
        }

        if (session.Status != UploadStatus.Pending)
            throw new InvalidOperationException($"Cannot complete session in status {session.Status}");

        if (session.IsExpired())
            throw new InvalidOperationException($"Upload session {uploadId} has expired");

        var won = await _repo.TryBeginCompleteAsync(uploadId, ct);
        if (!won)
        {
            session = await _repo.GetAsync(uploadId, ct)
                      ?? throw new InvalidOperationException($"Upload session {uploadId} not found");

            if (session.Status == UploadStatus.Completed)
                return session.FinalFileName ?? session.FileName;

            throw new InvalidOperationException(
                "Could not acquire complete lease (another node won CAS). Retry status shortly.");
        }

        session = await _repo.GetAsync(uploadId, ct)
                  ?? throw new InvalidOperationException($"Upload session {uploadId} not found");

        var (missing, bytesOnDisk) = await _storage.VerifyChunksParallelAsync(
            uploadId, session.TotalChunks, ct);

        if (missing.Count > 0)
        {
            await _repo.TryFailCompleteAsync(uploadId, session.Checksum, ct);
            _sessionCache.Remove(uploadId);
            var sample = missing.OrderBy(x => x).Take(20).ToArray();
            throw new InvalidOperationException(
                $"Not all chunks received. Expected {session.TotalChunks}, missing {missing.Count}. " +
                $"Missing (sample): [{string.Join(", ", sample)}]");
        }

        if (bytesOnDisk != session.TotalSize)
        {
            await _repo.TryFailCompleteAsync(uploadId, session.Checksum, ct);
            _sessionCache.Remove(uploadId);
            throw new InvalidOperationException(
                $"On-disk size mismatch. Expected {session.TotalSize} bytes, got {bytesOnDisk}.");
        }

        var expectedChecksum = NormalizeChecksum(checksum) ?? session.Checksum;
        var computeHash = _options.AlwaysComputeFullChecksum || expectedChecksum is not null;

        string finalPath;
        string actualChecksum;
        try
        {
            (finalPath, actualChecksum) = await _storage.MergeAsync(
                uploadId,
                session.FileName,
                session.TotalChunks,
                session.TotalSize,
                session.ChunkSize,
                computeHash,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Merge failed for upload {UploadId}", uploadId);
            await _repo.TryFailCompleteAsync(uploadId, session.Checksum, ct);
            _sessionCache.Remove(uploadId);
            _receivedCache.Remove(uploadId);
            _metrics.RecordFailed();
            _audit.UploadFailed(uploadId, session.FileName, "merge_failed", session.ClientIp);
            await SafePublishFailedAsync(session, "merge_failed", ct);
            throw;
        }

        if (expectedChecksum is not null &&
            !string.IsNullOrEmpty(actualChecksum) &&
            !string.Equals(actualChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Checksum mismatch for upload {UploadId}. Expected={Expected}, Actual={Actual}",
                uploadId, expectedChecksum, actualChecksum);

            await _storage.DeleteFinalFileAsync(Path.GetFileName(finalPath), ct);
            await _repo.TryFailCompleteAsync(uploadId, actualChecksum, ct);
            _sessionCache.Remove(uploadId);
            _receivedCache.Remove(uploadId);
            _metrics.RecordFailed();
            _audit.UploadFailed(uploadId, session.FileName, "checksum_mismatch", session.ClientIp);
            await SafePublishFailedAsync(session, "checksum_mismatch", ct);

            throw new InvalidOperationException(
                $"Checksum mismatch. Expected {expectedChecksum}, got {actualChecksum}");
        }

        var finalName = Path.GetFileName(finalPath);
        var checksumToStore = !string.IsNullOrEmpty(actualChecksum)
            ? actualChecksum
            : expectedChecksum;

        string? fingerprintToStore = session.ContentFingerprint;
        try
        {
            var sampleBytes = _options.ContentSampleBytes > 0
                ? _options.ContentSampleBytes
                : ContentFingerprint.DefaultSampleBytes;
            fingerprintToStore = await ContentFingerprint.ComputeFromFileAsync(
                finalPath, session.TotalSize, sampleBytes, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not compute content fingerprint for {UploadId}; keeping client value", uploadId);
        }

        var finished = await _repo.TryFinishCompleteAsync(
            uploadId, finalName, checksumToStore, fingerprintToStore, ct);
        if (!finished)
        {
            _logger.LogError(
                "CAS finish failed for {UploadId} after successful merge; manual check required. Path={Path}",
                uploadId, finalPath);
            throw new InvalidOperationException(
                "Merge finished but metadata CAS to Completed failed. Inspect storage and session status.");
        }

        _metrics.RecordCompleted(session.TotalSize);
        _receivedCache.Remove(uploadId);
        _sessionCache.Remove(uploadId);

        session.Status = UploadStatus.Completed;
        session.FinalFileName = finalName;
        session.Checksum = checksumToStore;
        session.ContentFingerprint = fingerprintToStore;
        session.CompletedAt = DateTime.UtcNow;

        _audit.UploadCompleted(
            session.Id, session.FileName, finalName, session.TotalSize, session.ClientIp);

        _logger.LogInformation(
            "Upload completed {UploadId} → {FinalFileName} (hash={HashMode}, clientExpected={HasExpected})",
            session.Id, finalName, computeHash ? "computed" : "skipped", expectedChecksum is not null);

        await SafePublishCompletedAsync(session, ct);

        return finalPath;
    }

    public async Task AbortAsync(Guid uploadId, CancellationToken ct = default)
    {
        _sessionCache.Remove(uploadId);

        var session = await _repo.GetAsync(uploadId, ct)
                     ?? throw new InvalidOperationException($"Upload session {uploadId} not found");

        if (session.Status is UploadStatus.Completed or UploadStatus.Aborted or UploadStatus.Expired)
            return;

        if (session.Status == UploadStatus.Completing)
            throw new InvalidOperationException("Cannot abort while complete/merge is in progress.");

        if (session.Status == UploadStatus.Failed)
            return;

        var won = await _repo.TryAbortAsync(uploadId, ct);
        if (!won)
        {
            session = await _repo.GetAsync(uploadId, ct);
            if (session is null || session.Status is UploadStatus.Aborted or UploadStatus.Completed
                or UploadStatus.Expired or UploadStatus.Failed)
                return;

            if (session.Status == UploadStatus.Completing)
                throw new InvalidOperationException("Cannot abort while complete/merge is in progress.");

            throw new InvalidOperationException($"Cannot abort session in status {session.Status}");
        }

        await _storage.DeleteTempFolderAsync(uploadId, ct);
        _receivedCache.Remove(uploadId);
        _sessionCache.Remove(uploadId);
        _metrics.RecordAborted();

        _audit.UploadAborted(uploadId, session.FileName, session.ClientIp);
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

    public async Task<UploadSession?> GetStatusAsync(Guid uploadId, CancellationToken ct = default)
    {
        var session = await _repo.GetAsync(uploadId, ct);
        if (session is not null)
            _sessionCache.Set(session);
        return session;
    }

    private async Task EnforceQuotaAsync(long totalSize, string? clientIp, CancellationToken ct)
    {
        if (_options.MaxTotalStoredBytes > 0)
        {
            var completed = await _repo.SumCompletedBytesAsync(ct);
            var pending = await _repo.SumActivePendingBytesAsync(ct);
            var used = completed + pending;
            if (used + totalSize > _options.MaxTotalStoredBytes)
            {
                var reason =
                    $"Global storage quota exceeded. Used≈{used} bytes, limit={_options.MaxTotalStoredBytes}, requested={totalSize}.";
                _audit.QuotaRejected(clientIp, reason, totalSize);
                throw new InvalidOperationException(reason);
            }
        }

        if (_options.MaxStoredBytesPerIp > 0 && !string.IsNullOrWhiteSpace(clientIp))
        {
            var completedIp = await _repo.SumCompletedBytesByIpAsync(clientIp, ct);
            var pendingIp = await _repo.SumActivePendingBytesByIpAsync(clientIp, ct);
            var usedIp = completedIp + pendingIp;
            if (usedIp + totalSize > _options.MaxStoredBytesPerIp)
            {
                var reason =
                    $"Per-IP storage quota exceeded. Used≈{usedIp} bytes, limit={_options.MaxStoredBytesPerIp}, requested={totalSize}.";
                _audit.QuotaRejected(clientIp, reason, totalSize);
                throw new InvalidOperationException(reason);
            }
        }
    }

    private async Task<UploadSession?> GetSessionHotAsync(Guid uploadId, CancellationToken ct)
    {
        if (_sessionCache.TryGet(uploadId, out var cached) &&
            cached.Status == UploadStatus.Pending &&
            !cached.IsExpired())
        {
            return cached;
        }

        var session = await _repo.GetAsync(uploadId, ct);
        if (session is not null)
            _sessionCache.Set(session);

        return session;
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
