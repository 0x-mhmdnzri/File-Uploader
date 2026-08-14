using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using WebApi.Interfaces;

namespace WebApi.Storages;

/// <summary>
/// S3-compatible IFileStorage (AWS S3, MinIO, Cloudflare R2).
/// Parts: {TempPrefix}{uploadId}/part{n}
/// Final: {FinalPrefix}{fileName}
/// Merge streams parts into a final PutObject (portable; not server-side compose).
/// </summary>
public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly ObjectStorageOptions _options;
    private readonly StorageOptions _storageOptions;
    private readonly IFileHasher _hasher;
    private readonly IAmazonS3 _s3;
    private readonly SemaphoreSlim _diskGate;
    private const int BufferSize = 1 * 1024 * 1024;

    public S3FileStorage(
        IOptions<ObjectStorageOptions> objectOptions,
        IOptions<StorageOptions> storageOptions,
        IFileHasher hasher)
    {
        _options = objectOptions.Value;
        _storageOptions = storageOptions.Value;
        _hasher = hasher;

        var maxIo = _storageOptions.MaxConcurrentDiskIo > 0
            ? _storageOptions.MaxConcurrentDiskIo
            : Math.Clamp(Environment.ProcessorCount, 2, 16);
        _diskGate = new SemaphoreSlim(maxIo, maxIo);

        var creds = new BasicAWSCredentials(_options.AccessKey, _options.SecretKey);
        var cfg = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(_options.Region),
            ForcePathStyle = _options.ForcePathStyle
        };

        if (!string.IsNullOrWhiteSpace(_options.ServiceUrl))
        {
            cfg.ServiceURL = _options.ServiceUrl;
            cfg.ForcePathStyle = true;
        }

        _s3 = new AmazonS3Client(creds, cfg);
    }

    public void Dispose()
    {
        _diskGate.Dispose();
        _s3.Dispose();
    }

    private string PartKey(Guid uploadId, int index) =>
        $"{_options.TempPrefix.TrimEnd('/')}/{uploadId}/part{index}";

    private string TempPrefix(Guid uploadId) =>
        $"{_options.TempPrefix.TrimEnd('/')}/{uploadId}/";

    private string FinalKey(string fileName) =>
        $"{_options.FinalPrefix.TrimEnd('/')}/{Path.GetFileName(fileName)}";

    public Task EnsureDirectoriesAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task SaveChunkAsync(Guid uploadId, int chunkIndex, Stream data, CancellationToken ct = default)
    {
        await _diskGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var put = new PutObjectRequest
            {
                BucketName = _options.Bucket,
                Key = PartKey(uploadId, chunkIndex),
                InputStream = data,
                AutoCloseStream = false
            };
            await _s3.PutObjectAsync(put, ct).ConfigureAwait(false);
        }
        finally
        {
            _diskGate.Release();
        }
    }

    public async Task DeleteChunkAsync(Guid uploadId, int chunkIndex, CancellationToken ct = default)
    {
        try
        {
            await _s3.DeleteObjectAsync(_options.Bucket, PartKey(uploadId, chunkIndex), ct)
                .ConfigureAwait(false);
        }
        catch (AmazonS3Exception)
        {
            // ignore missing
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
        var safeName = Path.GetFileName(fileName);
        var key = FinalKey(safeName);

        if (await ObjectExistsAsync(key, ct).ConfigureAwait(false))
            key = FinalKey($"{Path.GetFileNameWithoutExtension(safeName)}_{uploadId:N}{Path.GetExtension(safeName)}");

        var tempLocal = Path.Combine(Path.GetTempPath(), $"s3-merge-{uploadId:N}.bin");
        try
        {
            await using (var local = new FileStream(
                             tempLocal,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             BufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                local.SetLength(totalSize);
                using var sha = SHA256.Create();
                var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
                try
                {
                    long written = 0;
                    for (var i = 0; i < totalChunks; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var partKey = PartKey(uploadId, i);

                        // GetObjectResponse implements IDisposable only (not IAsyncDisposable).
                        using var part = await _s3.GetObjectAsync(_options.Bucket, partKey, ct)
                            .ConfigureAwait(false);
                        await using var partStream = part.ResponseStream;

                        int read;
                        while ((read = await partStream.ReadAsync(buffer.AsMemory(0, BufferSize), ct)
                                   .ConfigureAwait(false)) > 0)
                        {
                            await local.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                            sha.TransformBlock(buffer, 0, read, null, 0);
                            written += read;
                        }
                    }

                    await local.FlushAsync(ct).ConfigureAwait(false);
                    if (written != totalSize)
                        throw new InvalidOperationException(
                            $"S3 merge size mismatch. Expected {totalSize}, wrote {written}.");

                    sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    var hex = Convert.ToHexString(sha.Hash!).ToLowerInvariant();

                    local.Position = 0;
                    var put = new PutObjectRequest
                    {
                        BucketName = _options.Bucket,
                        Key = key,
                        InputStream = local,
                        AutoCloseStream = false
                    };
                    await _s3.PutObjectAsync(put, ct).ConfigureAwait(false);

                    await DeleteTempFolderAsync(uploadId, ct).ConfigureAwait(false);
                    return ($"s3://{_options.Bucket}/{key}", hex);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
        finally
        {
            if (File.Exists(tempLocal))
                File.Delete(tempLocal);
        }
    }

    public async Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
    {
        var key = filePath.StartsWith("s3://", StringComparison.OrdinalIgnoreCase)
            ? filePath.Split('/', 4).Last()
            : filePath;

        if (key.StartsWith(_options.Bucket + "/", StringComparison.Ordinal))
            key = key[(_options.Bucket.Length + 1)..];

        using var obj = await _s3.GetObjectAsync(_options.Bucket, key, ct).ConfigureAwait(false);
        await using var stream = obj.ResponseStream;
        return await _hasher.ComputeSha256Async(stream, ct).ConfigureAwait(false);
    }

    public async Task DeleteTempFolderAsync(Guid uploadId, CancellationToken ct = default)
    {
        var prefix = TempPrefix(uploadId);
        string? token = null;
        do
        {
            var list = await _s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _options.Bucket,
                Prefix = prefix,
                ContinuationToken = token
            }, ct).ConfigureAwait(false);

            if (list.S3Objects is { Count: > 0 })
            {
                var del = new DeleteObjectsRequest
                {
                    BucketName = _options.Bucket,
                    Objects = list.S3Objects.Select(o => new KeyVersion { Key = o.Key }).ToList()
                };
                await _s3.DeleteObjectsAsync(del, ct).ConfigureAwait(false);
            }

            token = list.IsTruncated == true ? list.NextContinuationToken : null;
        } while (token is not null);
    }

    public async Task DeleteFinalFileAsync(string fileName, CancellationToken ct = default)
    {
        var key = fileName.StartsWith("s3://", StringComparison.OrdinalIgnoreCase)
            ? fileName.Split('/', 4).Last()
            : FinalKey(fileName);

        try
        {
            await _s3.DeleteObjectAsync(_options.Bucket, key, ct).ConfigureAwait(false);
        }
        catch (AmazonS3Exception)
        {
            // ignore
        }
    }

    public Task<string> GetTempFolderAsync(Guid uploadId) =>
        Task.FromResult($"s3://{_options.Bucket}/{TempPrefix(uploadId)}");

    public async Task<bool> ChunkExistsAsync(Guid uploadId, int chunkIndex)
    {
        try
        {
            await _s3.GetObjectMetadataAsync(_options.Bucket, PartKey(uploadId, chunkIndex))
                .ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<IReadOnlyCollection<int>> GetExistingChunkIndexesAsync(
        Guid uploadId, CancellationToken ct = default)
    {
        var prefix = TempPrefix(uploadId);
        var indexes = new List<int>();
        string? token = null;

        do
        {
            var list = await _s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _options.Bucket,
                Prefix = prefix,
                ContinuationToken = token
            }, ct).ConfigureAwait(false);

            foreach (var obj in list.S3Objects ?? [])
            {
                var name = obj.Key[(obj.Key.LastIndexOf('/') + 1)..];
                if (name.StartsWith("part", StringComparison.Ordinal) &&
                    int.TryParse(name["part".Length..], out var idx))
                {
                    indexes.Add(idx);
                }
            }

            token = list.IsTruncated == true ? list.NextContinuationToken : null;
        } while (token is not null);

        return indexes;
    }

    public async Task<(IReadOnlyCollection<int> Missing, long BytesOnDisk)> VerifyChunksParallelAsync(
        Guid uploadId,
        int totalChunks,
        CancellationToken ct = default)
    {
        var missing = new ConcurrentBag<int>();
        long bytes = 0;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, _storageOptions.MergeParallelism),
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(Enumerable.Range(0, totalChunks), options, async (i, token) =>
        {
            try
            {
                var meta = await _s3.GetObjectMetadataAsync(_options.Bucket, PartKey(uploadId, i), token)
                    .ConfigureAwait(false);
                Interlocked.Add(ref bytes, meta.ContentLength);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                missing.Add(i);
            }
        }).ConfigureAwait(false);

        return (missing, bytes);
    }

    private async Task<bool> ObjectExistsAsync(string key, CancellationToken ct)
    {
        try
        {
            await _s3.GetObjectMetadataAsync(_options.Bucket, key, ct).ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
