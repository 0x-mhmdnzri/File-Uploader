
using Microsoft.Extensions.Options;
using WebApi.Interfaces;

namespace WebApi.Storages;
 
 
 
public class FileSystemStorage : IFileStorage
{
    private readonly StorageOptions _options;
    public FileSystemStorage(IOptions<StorageOptions> options) => _options = options.Value;

    public Task EnsureDirectoriesAsync()
    {
        Directory.CreateDirectory(_options.TempPath);
        Directory.CreateDirectory(_options.FinalPath);
        return Task.CompletedTask;
    }

    public async Task SaveChunkAsync(Guid uploadId, int chunkIndex, Stream data, CancellationToken ct)
    {
        var folder = Path.Combine(_options.TempPath, uploadId.ToString());
        Directory.CreateDirectory(folder);

        var filePath = Path.Combine(folder, $"{uploadId}.part{chunkIndex}");
        await using var fs = new FileStream(filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 32 * 1024 * 1024,
            useAsync: true);

        await data.CopyToAsync(fs, ct);
    }

    public async Task MergeAsync(Guid uploadId, string fileName, int totalChunks, Stream _, CancellationToken ct)
    {
        var folder = Path.Combine(_options.TempPath, uploadId.ToString());
        Directory.CreateDirectory(_options.FinalPath);
        var finalPath = Path.Combine(_options.FinalPath, fileName);

        var tasks = new List<Task>();

        await using var finalFs = new FileStream(finalPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 32 * 1024 * 1024,
            useAsync: true);

        var mergeLock = new object();

        // Parallel merge با محدودیت تعداد thread
        var maxParallel = Environment.ProcessorCount;
        using var semaphore = new SemaphoreSlim(maxParallel);

        for (var i = 0; i < totalChunks; i++)
        {
            var chunkIndex = i;
            await semaphore.WaitAsync(ct);

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var partPath = Path.Combine(folder, $"{uploadId}.part{chunkIndex}");
                    await using var partFs = new FileStream(partPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 32 * 1024 * 1024,
                        useAsync: true);

                    var buffer = new byte[32 * 1024 * 1024];
                    int read;
                    while ((read = await partFs.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    {
                        lock (mergeLock)
                        {
                            finalFs.Write(buffer, 0, read);
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        await Task.WhenAll(tasks);
        await finalFs.FlushAsync(ct);

        Directory.Delete(folder, recursive: true);
    }

    public Task<string> GetTempFolderAsync(Guid uploadId) =>
        Task.FromResult(Path.Combine(_options.TempPath, uploadId.ToString()));

    public Task<bool> ChunkExistsAsync(Guid uploadId, int chunkIndex) =>
        Task.FromResult(File.Exists(Path.Combine(_options.TempPath, uploadId.ToString(), $"{uploadId}.part{chunkIndex}")));
}

