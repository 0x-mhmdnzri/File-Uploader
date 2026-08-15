using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Extensions.Options;
using WebApi.Interfaces;

namespace WebApi.Storages;

/// <summary>
/// Own data-plane storage on a local or <b>shared</b> filesystem.
/// Part layout (P4.2 / D5): <c>{TempPath}/{uploadId}/part/{index}</c>
/// — stable, hierarchical, identical on every node that mounts the same volume.
/// Merge and verify only use these helpers (D9: no node-local-only assumptions).
/// </summary>
public sealed class FileSystemStorage : IFileStorage, IDisposable
{
    private readonly StorageOptions _options;
    private readonly IFileHasher _hasher;
    private readonly SemaphoreSlim _diskGate;
    private const int BufferSize = 1 * 1024 * 1024;

    public FileSystemStorage(IOptions<StorageOptions> options, IFileHasher hasher)
    {
        _options = options.Value;
        _hasher = hasher;

        var maxIo = _options.MaxConcurrentDiskIo > 0
            ? _options.MaxConcurrentDiskIo
            : Math.Clamp(Environment.ProcessorCount, 2, 16);

        _diskGate = new SemaphoreSlim(maxIo, maxIo);
    }

    public void Dispose() => _diskGate.Dispose();

    // -------------------------------------------------------------------------
    // P4.2 D5 — Stable part key helpers (single source of truth for paths)
    // Layout: {TempPath}/{uploadId}/part/{index}
    // -------------------------------------------------------------------------

    /// <summary>Session temp directory on the (possibly shared) volume.</summary>
    private string SessionTempDir(Guid uploadId) =>
        Path.Combine(_options.TempPath, uploadId.ToString("N"));

    /// <summary>Directory that holds all parts for one upload.</summary>
    private string PartDir(Guid uploadId) =>
        Path.Combine(SessionTempDir(uploadId), "part");

    /// <summary>
    /// Stable part path: <c>{TempPath}/{uploadId}/part/{index}</c>.
    /// Same key on every API node when TempPath is a shared mount (D6).
    /// </summary>
    private string PartPath(Guid uploadId, int chunkIndex) =>
        Path.Combine(PartDir(uploadId), chunkIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));

    // -------------------------------------------------------------------------

    public Task EnsureDirectoriesAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_options.TempPath);
        Directory.CreateDirectory(_options.FinalPath);
        return Task.CompletedTask;
    }

    public async Task SaveChunkAsync(Guid uploadId, int chunkIndex, Stream data, CancellationToken ct = default)
    {
        await _diskGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(PartDir(uploadId));

            var filePath = PartPath(uploadId, chunkIndex);

            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                await using var fs = new FileStream(
                    filePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: BufferSize,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan);

                int read;
                while ((read = await data.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }

                await fs.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        finally
        {
            _diskGate.Release();
        }
    }

    public Task DeleteChunkAsync(Guid uploadId, int chunkIndex, CancellationToken ct = default)
    {
        var path = PartPath(uploadId, chunkIndex);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<(string Path, string Sha256Hex)> MergeAsync(
        Guid uploadId,
        string fileName,
        int totalChunks,
        long totalSize,
        int chunkSize,
        CancellationToken ct = default)
    {
        // D9: merge only reads parts via PartPath — never a node-local scratch path.
        return _options.SinglePassMergeAndHash
            ? MergeSinglePassAsync(uploadId, fileName, totalChunks, totalSize, ct)
            : MergeParallelThenHashAsync(uploadId, fileName, totalChunks, totalSize, chunkSize, ct);
    }

    private async Task<(string Path, string Sha256Hex)> MergeSinglePassAsync(
        Guid uploadId,
        string fileName,
        int totalChunks,
        long totalSize,
        CancellationToken ct)
    {
        Directory.CreateDirectory(_options.FinalPath);
        var finalPath = ResolveFinalPath(uploadId, fileName);

        await _diskGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                await using var finalFs = new FileStream(
                    finalPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: BufferSize,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan);

                finalFs.SetLength(totalSize);

                using var sha = SHA256.Create();
                long written = 0;

                for (var i = 0; i < totalChunks; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var partPath = PartPath(uploadId, i);
                    if (!File.Exists(partPath))
                        throw new InvalidOperationException($"Missing chunk {i} for upload {uploadId}");

                    await using var partFs = new FileStream(
                        partPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: BufferSize,
                        options: FileOptions.Asynchronous | FileOptions.SequentialScan);

                    int read;
                    while ((read = await partFs.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
                    {
                        await finalFs.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        sha.TransformBlock(buffer, 0, read, null, 0);
                        written += read;
                    }
                }

                await finalFs.FlushAsync(ct).ConfigureAwait(false);

                if (written != totalSize)
                {
                    throw new InvalidOperationException(
                        $"Single-pass merge size mismatch. Expected {totalSize}, wrote {written}.");
                }

                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                var hex = Convert.ToHexString(sha.Hash!).ToLowerInvariant();

                await DeleteTempFolderAsync(uploadId, ct).ConfigureAwait(false);
                return (finalPath, hex);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        finally
        {
            _diskGate.Release();
        }
    }

    private async Task<(string Path, string Sha256Hex)> MergeParallelThenHashAsync(
        Guid uploadId,
        string fileName,
        int totalChunks,
        long totalSize,
        int chunkSize,
        CancellationToken ct)
    {
        Directory.CreateDirectory(_options.FinalPath);
        var finalPath = ResolveFinalPath(uploadId, fileName);

        await using (var pre = new FileStream(
                         finalPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 4096,
                         options: FileOptions.Asynchronous))
        {
            pre.SetLength(totalSize);
            await pre.FlushAsync(ct).ConfigureAwait(false);
        }

        var parallelism = Math.Max(1, _options.MergeParallelism);
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = parallelism,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(
            Enumerable.Range(0, totalChunks),
            options,
            async (i, token) =>
            {
                var partPath = PartPath(uploadId, i);
                if (!File.Exists(partPath))
                    throw new InvalidOperationException($"Missing chunk {i} for upload {uploadId}");

                var offset = (long)i * chunkSize;
                var partLen = new FileInfo(partPath).Length;
                var expectedLen = i == totalChunks - 1
                    ? totalSize - offset
                    : chunkSize;

                if (partLen != expectedLen)
                {
                    throw new InvalidOperationException(
                        $"Chunk {i} length {partLen} != expected {expectedLen}.");
                }

                await _diskGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
                    try
                    {
                        await using var partFs = new FileStream(
                            partPath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            bufferSize: BufferSize,
                            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

                        await using var finalFs = new FileStream(
                            finalPath,
                            FileMode.Open,
                            FileAccess.Write,
                            FileShare.ReadWrite,
                            bufferSize: BufferSize,
                            options: FileOptions.Asynchronous | FileOptions.RandomAccess);

                        finalFs.Seek(offset, SeekOrigin.Begin);

                        long copied = 0;
                        int read;
                        while ((read = await partFs.ReadAsync(buffer.AsMemory(0, BufferSize), token).ConfigureAwait(false)) > 0)
                        {
                            await finalFs.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                            copied += read;
                        }

                        await finalFs.FlushAsync(token).ConfigureAwait(false);

                        if (copied != partLen)
                        {
                            throw new InvalidOperationException(
                                $"Chunk {i} short write: copied {copied}, part length {partLen}.");
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
                finally
                {
                    _diskGate.Release();
                }
            }).ConfigureAwait(false);

        var finalInfo = new FileInfo(finalPath);
        if (finalInfo.Length != totalSize)
        {
            throw new InvalidOperationException(
                $"Parallel merge size mismatch. Expected {totalSize}, file length {finalInfo.Length}.");
        }

        var sha256Hex = await _hasher.ComputeSha256Async(finalPath, ct).ConfigureAwait(false);
        await DeleteTempFolderAsync(uploadId, ct).ConfigureAwait(false);
        return (finalPath, sha256Hex);
    }

    private string ResolveFinalPath(Guid uploadId, string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        var finalPath = Path.Combine(_options.FinalPath, safeName);

        if (File.Exists(finalPath))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(safeName);
            var ext = Path.GetExtension(safeName);
            finalPath = Path.Combine(_options.FinalPath, $"{nameWithoutExt}_{uploadId:N}{ext}");
        }

        return finalPath;
    }

    public Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
        => _hasher.ComputeSha256Async(filePath, ct);

    public Task DeleteTempFolderAsync(Guid uploadId, CancellationToken ct = default)
    {
        var folder = SessionTempDir(uploadId);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        return Task.CompletedTask;
    }

    public Task DeleteFinalFileAsync(string fileName, CancellationToken ct = default)
    {
        var path = Path.Combine(_options.FinalPath, Path.GetFileName(fileName));
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<string> GetTempFolderAsync(Guid uploadId) =>
        Task.FromResult(SessionTempDir(uploadId));

    public Task<bool> ChunkExistsAsync(Guid uploadId, int chunkIndex) =>
        Task.FromResult(File.Exists(PartPath(uploadId, chunkIndex)));

    public Task<IReadOnlyCollection<int>> GetExistingChunkIndexesAsync(Guid uploadId, CancellationToken ct = default)
    {
        var dir = PartDir(uploadId);
        if (!Directory.Exists(dir))
            return Task.FromResult<IReadOnlyCollection<int>>(Array.Empty<int>());

        var indexes = new List<int>();

        foreach (var file in Directory.EnumerateFiles(dir))
        {
            ct.ThrowIfCancellationRequested();

            var name = Path.GetFileName(file);
            if (name is null)
                continue;

            if (int.TryParse(name, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var index) && index >= 0)
            {
                indexes.Add(index);
            }
        }

        return Task.FromResult<IReadOnlyCollection<int>>(indexes);
    }

    public async Task<(IReadOnlyCollection<int> Missing, long BytesOnDisk)> VerifyChunksParallelAsync(
        Guid uploadId,
        int totalChunks,
        CancellationToken ct = default)
    {
        var missing = new ConcurrentBag<int>();
        long bytesOnDisk = 0;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, _options.MergeParallelism),
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(
            Enumerable.Range(0, totalChunks),
            options,
            (i, token) =>
            {
                var partPath = PartPath(uploadId, i);
                if (!File.Exists(partPath))
                    missing.Add(i);
                else
                    Interlocked.Add(ref bytesOnDisk, new FileInfo(partPath).Length);

                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);

        return (missing, bytesOnDisk);
    }
}
