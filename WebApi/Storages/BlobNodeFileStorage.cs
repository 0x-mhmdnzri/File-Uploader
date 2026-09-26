using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Globalization;
using Microsoft.Extensions.Options;
using WebApi.Interfaces;
using WebApi.Options;

namespace WebApi.Storages;

/// <summary>
/// <see cref="IFileStorage"/> backed by owned blob nodes (perf 4.1 / D8).
/// Part keys: <c>parts/{uploadId:N}/part/{index}</c>
/// Final keys: <c>files/{fileName}</c>
/// </summary>
public sealed class BlobNodeFileStorage : IFileStorage
{
    private readonly IBlobNodeResolver _resolver;
    private readonly IFileHasher _hasher;
    private readonly StorageOptions _options;
    private const int BufferSize = 4 * 1024 * 1024;

    public BlobNodeFileStorage(
        IBlobNodeResolver resolver,
        IFileHasher hasher,
        IOptions<StorageOptions> options)
    {
        _resolver = resolver;
        _hasher = hasher;
        _options = options.Value;
    }

    private static string PartKey(Guid uploadId, int index) =>
        $"parts/{uploadId:N}/part/{index.ToString(CultureInfo.InvariantCulture)}";

    private static string PartPrefix(Guid uploadId) =>
        $"parts/{uploadId:N}/part/";

    private static string FinalKey(string fileName) =>
        $"files/{Path.GetFileName(fileName)}";

    private IBlobNodeClient NodeFor(Guid uploadId) => _resolver.ResolveForUpload(uploadId);

    public Task EnsureDirectoriesAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task EnsureSessionDirectoriesAsync(Guid uploadId, CancellationToken ct = default) =>
        Task.CompletedTask;

    public async Task SaveChunkAsync(Guid uploadId, int chunkIndex, Stream data, CancellationToken ct = default)
    {
        var node = NodeFor(uploadId);
        var key = PartKey(uploadId, chunkIndex);
        long? len = data.CanSeek ? data.Length - data.Position : null;
        await node.PutObjectAsync(key, data, len, ct).ConfigureAwait(false);
    }

    public async Task SaveChunkAsync(Guid uploadId, int chunkIndex, PipeReader reader, CancellationToken ct = default)
    {
        // Materialize to a temporary file on the API node to avoid holding multi-GB in RAM,
        // then stream to the blob node. (Zero-copy end-to-end needs blob-node upload streaming.)
        var tmp = Path.Combine(Path.GetTempPath(), $"fu-part-{uploadId:N}-{chunkIndex}");
        try
        {
            await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None,
                             BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                while (true)
                {
                    var result = await reader.ReadAsync(ct).ConfigureAwait(false);
                    var buffer = result.Buffer;
                    if (buffer.IsEmpty && result.IsCompleted) break;
                    foreach (var segment in buffer)
                        await fs.WriteAsync(segment, ct).ConfigureAwait(false);
                    reader.AdvanceTo(buffer.End);
                    if (result.IsCompleted) break;
                }
                await fs.FlushAsync(ct).ConfigureAwait(false);
            }
            await reader.CompleteAsync().ConfigureAwait(false);

            await using var read = new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await SaveChunkAsync(uploadId, chunkIndex, read, ct).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    public async Task DeleteChunkAsync(Guid uploadId, int chunkIndex, CancellationToken ct = default)
    {
        await NodeFor(uploadId).DeleteObjectAsync(PartKey(uploadId, chunkIndex), ct)
            .ConfigureAwait(false);
    }

    public async Task<(string Path, string Sha256Hex)> MergeAsync(
        Guid uploadId,
        string fileName,
        int totalChunks,
        long totalSize,
        int chunkSize,
        bool computeHash = true,
        CancellationToken ct = default)
    {
        var node = NodeFor(uploadId);
        var partKeys = Enumerable.Range(0, totalChunks).Select(i => PartKey(uploadId, i)).ToList();
        var finalKey = FinalKey(ResolveFinalName(uploadId, fileName));

        // Preferred: blob-node server-side compose
        var composed = await node.TryComposeAsync(partKeys, finalKey, ct).ConfigureAwait(false);
        if (!composed)
        {
            // Fallback: stream parts through API process into final object
            await using var buffer = new FileStream(
                Path.Combine(Path.GetTempPath(), $"fu-merge-{uploadId:N}"),
                FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                BufferSize, FileOptions.Asynchronous | FileOptions.DeleteOnClose);

            var poolBuf = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                for (var i = 0; i < totalChunks; i++)
                {
                    await using var part = await node.GetObjectAsync(PartKey(uploadId, i), ct)
                        .ConfigureAwait(false);
                    int read;
                    while ((read = await part.ReadAsync(poolBuf.AsMemory(0, BufferSize), ct)
                               .ConfigureAwait(false)) > 0)
                    {
                        await buffer.WriteAsync(poolBuf.AsMemory(0, read), ct).ConfigureAwait(false);
                    }
                }
                await buffer.FlushAsync(ct).ConfigureAwait(false);
                buffer.Position = 0;
                await node.PutObjectAsync(finalKey, buffer, buffer.Length, ct).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(poolBuf);
            }
        }

        string sha = string.Empty;
        if (computeHash)
        {
            await using var finalStream = await node.GetObjectAsync(finalKey, ct).ConfigureAwait(false);
            sha = await _hasher.ComputeSha256Async(finalStream, ct).ConfigureAwait(false);
        }

        // Best-effort cleanup of parts
        await DeleteTempFolderAsync(uploadId, ct).ConfigureAwait(false);

        // Return logical path (blob key) — not a local filesystem path
        return (finalKey, sha);
    }

    public async Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
    {
        // filePath may be a blob key (files/...) or legacy local path
        if (filePath.StartsWith("files/", StringComparison.Ordinal) ||
            filePath.StartsWith("parts/", StringComparison.Ordinal))
        {
            // Try each node — final objects live on the node that composed them
            foreach (var node in _resolver.All)
            {
                if (await node.ExistsAsync(filePath, ct).ConfigureAwait(false))
                {
                    await using var s = await node.GetObjectAsync(filePath, ct).ConfigureAwait(false);
                    return await _hasher.ComputeSha256Async(s, ct).ConfigureAwait(false);
                }
            }
            throw new FileNotFoundException("Blob object not found on any node", filePath);
        }

        return await _hasher.ComputeSha256Async(filePath, ct).ConfigureAwait(false);
    }

    public async Task DeleteTempFolderAsync(Guid uploadId, CancellationToken ct = default)
    {
        var node = NodeFor(uploadId);
        var keys = await node.ListKeysAsync(PartPrefix(uploadId), ct).ConfigureAwait(false);
        foreach (var key in keys)
            await node.DeleteObjectAsync(key, ct).ConfigureAwait(false);
    }

    public async Task DeleteFinalFileAsync(string fileName, CancellationToken ct = default)
    {
        var key = fileName.StartsWith("files/", StringComparison.Ordinal)
            ? fileName
            : FinalKey(fileName);
        foreach (var node in _resolver.All)
        {
            if (await node.ExistsAsync(key, ct).ConfigureAwait(false))
            {
                await node.DeleteObjectAsync(key, ct).ConfigureAwait(false);
                return;
            }
        }
    }

    public async Task<bool> FinalObjectExistsAsync(string fileName, long? expectedSize = null, CancellationToken ct = default)
    {
        var key = fileName.StartsWith("files/", StringComparison.Ordinal)
            ? fileName
            : FinalKey(fileName);
        foreach (var node in _resolver.All)
        {
            if (await node.ExistsAsync(key, ct).ConfigureAwait(false))
                return true; // size check would need HEAD Content-Length — optional later
        }
        return false;
    }

    public Task<string> GetTempFolderAsync(Guid uploadId) =>
        Task.FromResult(PartPrefix(uploadId));

    public async Task<bool> ChunkExistsAsync(Guid uploadId, int chunkIndex)
    {
        return await NodeFor(uploadId).ExistsAsync(PartKey(uploadId, chunkIndex)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<int>> GetExistingChunkIndexesAsync(Guid uploadId, CancellationToken ct = default)
    {
        var keys = await NodeFor(uploadId).ListKeysAsync(PartPrefix(uploadId), ct).ConfigureAwait(false);
        var list = new List<int>();
        var prefix = PartPrefix(uploadId);
        foreach (var key in keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(key.AsSpan(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
            {
                list.Add(idx);
            }
        }
        return list;
    }

    public async Task<(IReadOnlyCollection<int> Missing, long BytesOnDisk)> VerifyChunksParallelAsync(
        Guid uploadId,
        int totalChunks,
        CancellationToken ct = default)
    {
        var node = NodeFor(uploadId);
        var missing = new ConcurrentBag<int>();
        long bytes = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, totalChunks),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, _options.MergeParallelism),
                CancellationToken = ct
            },
            async (i, token) =>
            {
                var size = await node.TryGetSizeAsync(PartKey(uploadId, i), token).ConfigureAwait(false);
                if (size is null)
                {
                    missing.Add(i);
                    return;
                }
                Interlocked.Add(ref bytes, size.Value);
            }).ConfigureAwait(false);

        return (missing, bytes);
    }

    private string ResolveFinalName(Guid uploadId, string fileName)
    {
        var safe = Path.GetFileName(fileName);
        // Uniqueness: if collision, blob node compose key includes upload id in adapter layer
        return safe;
    }
}
