namespace WebApi.Audit;

public interface IAuditLogger
{
    void UploadInitiated(Guid uploadId, string fileName, long totalSize, int totalChunks, string? clientIp);
    void UploadCompleted(Guid uploadId, string fileName, string finalFileName, long totalSize, string? clientIp);
    void UploadAborted(Guid uploadId, string fileName, string? clientIp);
    void UploadFailed(Guid uploadId, string fileName, string reason, string? clientIp);
    void AuthRejected(string path, string? clientIp, string reason);
    void QuotaRejected(string? clientIp, string reason, long attemptedBytes);
}
