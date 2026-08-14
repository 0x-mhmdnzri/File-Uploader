using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Options;
using WebApi.Interfaces;

namespace WebApi.Storages;

public sealed class FileSystemStorage : IFileStorage, IDisposable
{
    private readonly StorageOptions _options;
    private readonly IFileHasher _hasher;
    private readonly SemaphoreSlim _diskGate;
    private const int BufferSize = 1 * 1024 * 1024; // 1 MB copy buffer

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
            var folder = Path.Combine(_options.TempPath, uploadId.ToString());
            Directory.CreateDirectory(folder);

            var filePath = Path.Combine(folder, $"{uploadId}.part{chunkIndex}");

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

    public async Task<(string Path, string Sha256Hex)> MergeAsync(
        Guid uploadId,
        string fileName,
        int totalChunks,
        long totalSize,
        int chunkSize,
        CancellationToken ct = default)
    {
        var folder = Path.Combine(_options.TempPath, uploadId.ToString());
        Directory.CreateDirectory(_options.FinalPath);

        var safeName = Path.GetFileName(fileName);
        var finalPath = Path.Combine(_options.FinalPath, safeName);

        if (File.Exists(finalPath))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(safeName);
            var ext = Path.GetExtension(safeName);
            finalPath = Path.Combine(_options.FinalPath, $"{nameWithoutExt}_{uploadId:N}{ext}");
        }

        // Pre-allocate exact size so each worker can seek + write independently.
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
                var partPath = Path.Combine(folder, $"{uploadId}.part{i}");
                if (!File.Exists(partPath))
                    throw new InvalidOperationException($"Missing chunk {i} for upload {uploadId}");

                var offset = (long)i * chunkSize;

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

                        int read;
                        while ((read = await partFs.ReadAsync(buffer.AsMemory(0, BufferSize), token).ConfigureAwait(false)) > 0)
                        {
                            await finalFs.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                        }

                        await finalFs.FlushAsync(token).ConfigureAwait(false);
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

        // Integrated sequential hash — service layer does not open the file again.
        var sha256Hex = await _hasher.ComputeSha256Async(finalPath, ct).ConfigureAwait(false);

        await DeleteTempFolderAsync(uploadId, ct).ConfigureAwait(false);

        return (finalPath, sha256Hex);
    }

    public Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
        => _hasher.ComputeSha256Async(filePath, ct);

    public Task DeleteTempFolderAsync(Guid uploadId, CancellationToken ct = default)
    {
        var folder = Path.Combine(_options.TempPath, uploadId.ToString());
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
        Task.FromResult(Path.Combine(_options.TempPath, uploadId.ToString()));

    public Task<bool> ChunkExistsAsync(Guid uploadId, int chunkIndex) =>
        Task.FromResult(File.Exists(
            Path.Combine(_options.TempPath, uploadId.ToString(), $"{uploadId}.part{chunkIndex}")));

    public Task<IReadOnlyCollection<int>> GetExistingChunkIndexesAsync(Guid uploadId, CancellationToken ct = default)
    {
        var folder = Path.Combine(_options.TempPath, uploadId.ToString());
        if (!Directory.Exists(folder))
            return Task.FromResult<IReadOnlyCollection<int>>(Array.Empty<int>());

        var prefix = $"{uploadId}.part";
        var indexes = new List<int>();

        foreach (var file in Directory.EnumerateFiles(folder, $"{prefix}*"))
        {
            ct.ThrowIfCancellationRequested();

            var name = Path.GetFileName(file);
            if (name is null || !name.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var indexPart = name[prefix.Length..];
            if (int.TryParse(indexPart, out var index) && index >= 0)
                indexes.Add(index);
        }

        return Task.FromResult<IReadOnlyCollection<int>>(indexes);
    }

    public async Task<(IReadOnlyCollection<int> Missing, long BytesOnDisk)> VerifyChunksParallelAsync(
        Guid uploadId,
        int totalChunks,
        CancellationToken ct = default)
    {
        var folder = Path.Combine(_options.TempPath, uploadId.ToString());
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
                var partPath = Path.Combine(folder, $"{uploadId}.part{i}");
                if (!File.Exists(partPath))
                {
                    missing.Add(i);
                }
                else
                {
                    var len = new FileInfo(partPath).Length;
                    Interlocked.Add(ref bytesOnDisk, len);
                }

                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);

        return (missing, bytesOnDisk);
    }
}
