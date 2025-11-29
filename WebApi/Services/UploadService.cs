using WebApi.Domain;
using WebApi.Interfaces;
using WebApi.Storages;

namespace WebApi.Services;

public class UploadService : IUploadService
{
    private readonly IUploadRepository _repo;
    private readonly IFileStorage _storage;

    public UploadService(IUploadRepository repo, IFileStorage storage)
    {
        _repo = repo;
        _storage = storage;
    }

    public async Task<UploadSession> InitiateAsync(string fileName, long totalSize, int chunkSize)
    {
        await _storage.EnsureDirectoriesAsync();
        var session = new UploadSession
        {
            Id = Guid.NewGuid(),
            FileName = Path.GetFileName(fileName),
            TotalSize = totalSize,
            ChunkSize = chunkSize,
            TotalChunks = (int)Math.Ceiling((double)totalSize / chunkSize),
            TempFolder = await _storage.GetTempFolderAsync(Guid.NewGuid())
        };
        session.TempFolder = await _storage.GetTempFolderAsync(session.Id);
        await _repo.AddAsync(session);
        return session;
    }

    public async Task MarkChunkReceivedAsync(Guid uploadId, int chunkIndex)
    {
        var s = await _repo.GetAsync(uploadId);
        if (s == null) throw new InvalidOperationException("not found");
        s.ReceivedChunks[chunkIndex] = true;
        await _repo.UpdateAsync(s);
    }

    public async Task MergeChunksAsync(Guid uploadId)
    {
        var s = await _repo.GetAsync(uploadId);
        if (s == null) throw new InvalidOperationException("not found");
        await using var ms = new MemoryStream();
        await _storage.MergeAsync(uploadId, s.FileName, s.TotalChunks, ms, CancellationToken.None);
        s.Completed = true;
        await _repo.UpdateAsync(s);
    }

    public Task<UploadSession> GetStatusAsync(Guid uploadId)
    {
        return _repo.GetAsync(uploadId);
    }
}