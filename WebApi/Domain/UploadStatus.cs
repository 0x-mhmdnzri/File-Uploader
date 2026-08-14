namespace WebApi.Domain;

public enum UploadStatus
{
    Pending = 0,
    Completed = 1,
    Expired = 2,
    Aborted = 3,
    Failed = 4
}
