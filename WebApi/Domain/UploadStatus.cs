namespace WebApi.Domain;

public enum UploadStatus
{
    Pending = 0,
    Completed = 1,
    Expired = 2,
    Aborted = 3,
    Failed = 4,

    /// <summary>
    /// Exclusive merge in progress (CAS from Pending). Prevents double-complete across nodes.
    /// </summary>
    Completing = 5
}
