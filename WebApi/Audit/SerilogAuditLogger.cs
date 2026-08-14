namespace WebApi.Audit;

/// <summary>
/// Structured audit trail via Serilog (searchable EventType property).
/// </summary>
public sealed class SerilogAuditLogger : IAuditLogger
{
    private readonly ILogger<SerilogAuditLogger> _logger;

    public SerilogAuditLogger(ILogger<SerilogAuditLogger> logger)
    {
        _logger = logger;
    }

    public void UploadInitiated(Guid uploadId, string fileName, long totalSize, int totalChunks, string? clientIp) =>
        _logger.LogInformation(
            "Audit {EventType} UploadId={UploadId} FileName={FileName} TotalSize={TotalSize} TotalChunks={TotalChunks} ClientIp={ClientIp}",
            "UploadInitiated", uploadId, fileName, totalSize, totalChunks, clientIp ?? "-");

    public void UploadCompleted(Guid uploadId, string fileName, string finalFileName, long totalSize, string? clientIp) =>
        _logger.LogInformation(
            "Audit {EventType} UploadId={UploadId} FileName={FileName} FinalFileName={FinalFileName} TotalSize={TotalSize} ClientIp={ClientIp}",
            "UploadCompleted", uploadId, fileName, finalFileName, totalSize, clientIp ?? "-");

    public void UploadAborted(Guid uploadId, string fileName, string? clientIp) =>
        _logger.LogInformation(
            "Audit {EventType} UploadId={UploadId} FileName={FileName} ClientIp={ClientIp}",
            "UploadAborted", uploadId, fileName, clientIp ?? "-");

    public void UploadFailed(Guid uploadId, string fileName, string reason, string? clientIp) =>
        _logger.LogWarning(
            "Audit {EventType} UploadId={UploadId} FileName={FileName} Reason={Reason} ClientIp={ClientIp}",
            "UploadFailed", uploadId, fileName, reason, clientIp ?? "-");

    public void AuthRejected(string path, string? clientIp, string reason) =>
        _logger.LogWarning(
            "Audit {EventType} Path={Path} ClientIp={ClientIp} Reason={Reason}",
            "AuthRejected", path, clientIp ?? "-", reason);

    public void QuotaRejected(string? clientIp, string reason, long attemptedBytes) =>
        _logger.LogWarning(
            "Audit {EventType} ClientIp={ClientIp} Reason={Reason} AttemptedBytes={AttemptedBytes}",
            "QuotaRejected", clientIp ?? "-", reason, attemptedBytes);
}
