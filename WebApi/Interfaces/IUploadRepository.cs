using WebApi.Domain;

namespace WebApi.Interfaces;

public interface IUploadRepository
{
    Task AddAsync(UploadSession session);
    Task<UploadSession> GetAsync(Guid id);
    Task UpdateAsync(UploadSession session);
}