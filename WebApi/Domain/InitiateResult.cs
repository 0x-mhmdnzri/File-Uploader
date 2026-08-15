namespace WebApi.Domain;

/// <summary>
/// Result of initiate — either a new pending session or a hit on an existing completed object.
/// </summary>
public sealed class InitiateResult
{
    public required UploadSession Session { get; init; }

    /// <summary>
    /// True when the same content (SHA-256 + size) already exists as Completed and the final file is present.
    /// Client should not upload chunks.
    /// </summary>
    public bool AlreadyExists { get; init; }

    /// <summary>
    /// Final object path or file name when <see cref="AlreadyExists"/>.
    /// </summary>
    public string? ExistingPath { get; init; }
}
