using Microsoft.AspNetCore.Mvc;
using WebApi.Interfaces;

namespace WebApi.Controllers;

[ApiController]
[Route("api/uploads")]
public class UploadController : ControllerBase
{
    private readonly IUploadService _service;
    private readonly IFileStorage _storage;

    public UploadController(IUploadService service, IFileStorage storage)
    {
        _service = service;
        _storage = storage;
    }

    /// <summary>
    /// Start a new chunked upload session.
    /// </summary>
    [HttpPost("initiate")]
    public async Task<IActionResult> Initiate(
        [FromForm] string fileName,
        [FromForm] long totalSize,
        [FromForm] int chunkSize = 16_777_216,
        [FromForm] string? contentType = null,
        CancellationToken ct = default)
    {
        var session = await _service.InitiateAsync(fileName, totalSize, chunkSize, contentType, ct);

        return Ok(new
        {
            uploadId = session.Id,
            chunkSize = session.ChunkSize,
            totalChunks = session.TotalChunks,
            expiresAt = session.ExpiresAt
        });
    }

    /// <summary>
    /// Upload a single chunk.
    /// </summary>
    [HttpPut("{uploadId:guid}/chunk/{index:int}")]
    [RequestSizeLimit(100_000_000)] // 100 MB safety limit per request
    public async Task<IActionResult> UploadChunk(
        [FromRoute] Guid uploadId,
        [FromRoute] int index,
        CancellationToken ct = default)
    {
        await _storage.SaveChunkAsync(uploadId, index, Request.Body, ct);
        await _service.MarkChunkReceivedAsync(uploadId, index, ct);
        return Ok();
    }

    /// <summary>
    /// Finalize the upload: merge chunks and mark as Completed.
    /// </summary>
    [HttpPost("{uploadId:guid}/complete")]
    public async Task<IActionResult> Complete(
        [FromRoute] Guid uploadId,
        CancellationToken ct = default)
    {
        var finalPath = await _service.CompleteAsync(uploadId, ct);
        return Ok(new { path = finalPath });
    }

    /// <summary>
    /// Abort an in-progress upload and delete temp data.
    /// </summary>
    [HttpDelete("{uploadId:guid}")]
    public async Task<IActionResult> Abort(
        [FromRoute] Guid uploadId,
        CancellationToken ct = default)
    {
        await _service.AbortAsync(uploadId, ct);
        return NoContent();
    }

    /// <summary>
    /// Get current status and list of received chunks (for resume).
    /// </summary>
    [HttpGet("{uploadId:guid}/status")]
    public async Task<IActionResult> Status(
        [FromRoute] Guid uploadId,
        CancellationToken ct = default)
    {
        var session = await _service.GetStatusAsync(uploadId, ct);
        if (session is null)
            return NotFound();

        var received = session.GetReceivedChunks().OrderBy(x => x).ToArray();

        return Ok(new
        {
            session.Id,
            session.FileName,
            session.FinalFileName,
            session.TotalSize,
            session.ChunkSize,
            session.TotalChunks,
            status = session.Status.ToString(),
            received,
            receivedCount = received.Length,
            session.CreatedAt,
            session.CompletedAt,
            session.ExpiresAt,
            isExpired = session.IsExpired()
        });
    }
}
