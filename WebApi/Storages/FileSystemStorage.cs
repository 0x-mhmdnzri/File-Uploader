using System.Collections.Concurrent;
using System.Buffers;
using Microsoft.Extensions.Options;
using WebApi.Interfaces;

namespace WebApi.Storages;

public sealed class FileSystemStorage : IFileStorage
{
    private readonly StorageOptions _options;

    // Limit concurrent disk IO to avoid thrashing
    private static readonly SemaphoreSlim DiskIoGate = new(Math.Max(4, Environment.ProcessorCount * 2));

    public FileSystemStorage(IOptions<StorageOptions> options) => _options = options.Value;

    public Task EnsureDirectoriesAsync()
    {
        Directory.CreateDirectory(_options.TempPath);
        Directory.CreateDirectory(_options.FinalPath);
        return Task.CompletedTask;
    }

    public async Task SaveChunkAsync(Guid uploadId, int chunkIndex, Stream data, CancellationToken ct)
    {
        await DiskIoGate.WaitAsync(ct);
        try
        {
            var folder = Path.Combine(_options.TempPath, uploadId.ToString());
            Directory.CreateDirectory(folder);

            var filePath = Path.Combine(folder, $"{uploadId}.part{chunkIndex}");

            await using var fs = new FileStream(
                filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4 * 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await data.CopyToAsync(fs, 4 * 1024 * 1024, ct);
            await fs.FlushAsync(ct);
        }
        finally
        {
            DiskIoGate.Release();
        }
    }

    public async Task MergeAsync(Guid uploadId, string fileName, int totalChunks, Stream _, CancellationToken ct)
    {
        var folder = Path.Combine(_options.TempPath, uploadId.ToString());
        Directory.CreateDirectory(_options.FinalPath);
        var finalPath = Path.Combine(_options.FinalPath, fileName);

        // 1. Parallel verification of existence + size collection (ConcurrentBag + ConcurrentDictionary)
        var missing = new ConcurrentBag();
        var chunkSizes = new ConcurrentDictionary<int, long>();
        long totalExpectedSize = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, totalChunks),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2, CancellationToken = ct },
            async (i, token) =>
            {
                var partPath = Path.Combine(folder, $"{uploadId}.part{i}");
                if (!File.Exists(partPath))
                {
                    missing.Add(i);
                    return;
                }

                var len = new FileInfo(partPath).Length;
                chunkSizes[i] = len;
                Interlocked.Add(ref totalExpectedSize, len);
            });

        if (!missing.IsEmpty)
        {
            var sample = string.Join(", ", missing.OrderBy(x => x).Take(30));
            throw new InvalidOperationException(
                $"Cannot merge: {missing.Count} chunk(s) missing. Sample: {sample}");
        }

        // 2. Pre-allocate final file (huge latency win – avoids progressive growth)
        await using (var pre = new FileStream(
            finalPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous))
        {
            pre.SetLength(totalExpectedSize);
            await pre.FlushAsync(ct);
        }

        // 3. True parallel offset-based writes (NO global lock)
        // Each task opens its own handle and writes only its range.
        var writeErrors = new ConcurrentBag();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, totalChunks),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount),
                CancellationToken = ct
            },
            async (chunkIndex, token) =>
            {
                await DiskIoGate.WaitAsync(token);
                try
                {
                    // Calculate absolute offset
                    long offset = 0;
                    for (int j = 0; j < chunkIndex; j++)
                        offset += chunkSizes[j];

                    var partPath = Path.Combine(folder, $"{uploadId}.part{chunkIndex}");

                    await using var partFs = new FileStream(
                        partPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 4 * 1024 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);

                    await using var finalFs = new FileStream(
                        finalPath,
                        FileMode.Open,
                        FileAccess.Write,
                        FileShare.ReadWrite,          // concurrent writers OK on different offsets
                        bufferSize: 4 * 1024 * 1024,
                        FileOptions.Asynchronous | FileOptions.RandomAccess);

                    finalFs.Seek(offset, SeekOrigin.Begin);

                    // ArrayPool + Memory/Span – zero extra GC pressure
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(4 * 1024 * 1024);
                    try
                    {
                        int read;
                        while ((read = await partFs.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
                        {
                            await finalFs.WriteAsync(buffer.AsMemory(0, read), token);
                        }
                        await finalFs.FlushAsync(token);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
                catch (Exception ex)
                {
                    writeErrors.Add(chunkIndex);
                    throw new InvalidOperationException($"Failed writing chunk {chunkIndex}: {ex.Message}", ex);
                }
                finally
                {
                    DiskIoGate.Release();
                }
            });

        if (!writeErrors.IsEmpty)
        {
            throw new InvalidOperationException(
                $"Merge failed for chunks: {string.Join(", ", writeErrors.OrderBy(x => x))}");
        }

        // 4. Final size verification (consistency)
        var finalInfo = new FileInfo(finalPath);
        if (finalInfo.Length != totalExpectedSize)
        {
            throw new InvalidOperationException(
                $"Final file size mismatch. Expected {totalExpectedSize:N0}, got {finalInfo.Length:N0}");
        }

        // Cleanup
        try { Directory.Delete(folder, recursive: true); }
        catch { /* best effort */ }
    }

    public Task<string> GetTempFolderAsync(Guid uploadId) =>
        Task.FromResult(Path.Combine(_options.TempPath, uploadId.ToString()));

    public Task<bool> ChunkExistsAsync(Guid uploadId, int chunkIndex) =>
        Task.FromResult(File.Exists(Path.Combine(_options.TempPath, uploadId.ToString(), $"{uploadId}.part{chunkIndex}")));
}
