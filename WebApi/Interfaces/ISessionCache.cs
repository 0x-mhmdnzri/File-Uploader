using WebApi.Domain;

namespace WebApi.Interfaces;

/// <summary>
/// Short-TTL process cache for hot-path session reads (chunk PUT).
/// </summary>
public interface ISessionCache
{
    bool TryGet(Guid uploadId, out UploadSession session);

    void Set(UploadSession session);

    void Remove(Guid uploadId);
}
