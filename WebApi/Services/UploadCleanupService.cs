namespace WebApi.Services;

// Obsolete: cleanup is owned solely by WebApi.BackgroundServices.OrphanCleanupService.
// This type is kept only so old project references do not break; it is not registered in DI.
[Obsolete("Use OrphanCleanupService only.")]
public sealed class UploadCleanupService
{
    private UploadCleanupService() { }
}
