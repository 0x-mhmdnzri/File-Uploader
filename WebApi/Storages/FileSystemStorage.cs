using Microsoft.Extensions.Options;
using WebApi.Interfaces;

namespace WebApi.Storages;

public class FileSystemStorage : IFileStorage
{
    private readonly StorageOptions _options;
    private const int BufferSize = 1 * 1024 * 1024; // 1 MB – good balance for sequential IO

    public FileSystemStorage(IOptions<StorageOptions> options)
    {
        _options = options.Value;
    }

    public Task EnsureDirectoriesAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_options.TempPath);
        Directory.CreateDirectory(_options.FinalPath);
        return Task.CompletedTask;
    }

    public async Task SaveChunkAsync(Guid uploadId, int chunkIndex, Stream data, CancellationToken ct = default)
    {
        var folder = Path.Combine(_options.TempPath, uploadId.ToString());
        Directory.CreateDirectory(folder);

        var filePath = Path.Combine(folder, $"{uploadId}.part{chunkIndex}");

        await using var fs = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: BufferSize,
            useAsync: true);

        await data.CopyToAsync(fs, BufferSize, ct);
    }

    public async Task<string> MergeAsync(Guid uploadId, string fileName, int totalChunks, CancellationToken ct = default)
    {
        var folder = Path.Combine(_options.TempPath, uploadId.ToString());
        Directory.CreateDirectory(_options.FinalPath);

        // Avoid name collisions by prefixing with uploadId if needed
        var safeName = Path.GetFileName(fileName);
        var finalPath = Path.Combine(_options.FinalPath, safeName);

        if (File.Exists(finalPath))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(safeName);
            var ext = Path.GetExtension(safeName);
            finalPath = Path.Combine(_options.FinalPath, $"{nameWithoutExt}_{uploadId:N}{ext}");
        }

        // Sequential stream merge – fastest and safest pattern
        await using (var finalFs = new FileStream(
                         finalPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: BufferSize,
                         useAsync: true))
        {
            for (var i = 0; i < totalChunks; i++)
            {
                ct.ThrowIfCancellationRequested();

                var partPath = Path.Combine(folder, $"{uploadId}.part{i}");
                if (!File.Exists(partPath))
                    throw new InvalidOperationException($"Missing chunk {i} for upload {uploadId}");

                await using var partFs = new FileStream(
                    partPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: BufferSize,
                    useAsync: true);

                await partFs.CopyToAsync(finalFs, BufferSize, ct);
            }

            await finalFs.FlushAsync(ct);
        }

        // Clean temp folder only after successful merge
        await DeleteTempFolderAsync(uploadId, ct);

        return finalPath;
    }

    public Task DeleteTempFolderAsync(Guid uploadId, CancellationToken ct = default)
    {
        var folder = Path.Combine(_options.TempPath, uploadId.ToString());
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
        return Task.CompletedTask;
    }

    public Task DeleteFinalFileAsync(string fileName, CancellationToken ct = default)
    {
        var path = Path.Combine(_options.FinalPath, Path.GetFileName(fileName));
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    public Task<string> GetTempFolderAsync(Guid uploadId) =>
        Task.FromResult(Path.Combine(_options.TempPath, uploadId.ToString()));

    public Task<bool> ChunkExistsAsync(Guid uploadId, int chunkIndex) =>
        Task.FromResult(File.Exists(
            Path.Combine(_options.TempPath, uploadId.ToString(), $"{uploadId}.part{chunkIndex}")));
}
