using Microsoft.AspNetCore.Mvc;
using WebApi.Infrastructure;
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
    /// Optional: pass checksum (hex SHA-256) to be verified after merge.
    /// </summary>
    [HttpPost("initiate")]
    public async Task<IActionResult> Initiate(
        [FromForm] string fileName,
        [FromForm] long totalSize,
        [FromForm] int chunkSize = 16_777_216,
        [FromForm] string? contentType = null,
        [FromForm] string? checksum = null,
        CancellationToken ct = default)
    {
        try
        {
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();

            var session = await _service.InitiateAsync(
                fileName, totalSize, chunkSize, contentType, checksum, clientIp, ct);

            return Ok(new
            {
                uploadId = session.Id,
                chunkSize = session.ChunkSize,
                totalChunks = session.TotalChunks,
                expiresAt = session.ExpiresAt
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Upload a single chunk.
    /// Optional Content-Encoding: gzip | deflate | br (per-chunk transport compression).
    /// Body is decompressed before storage so parts remain raw.
    /// </summary>
    [HttpPut("{uploadId:guid}/chunk/{index:int}")]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> UploadChunk(
        [FromRoute] Guid uploadId,
        [FromRoute] int index,
        CancellationToken ct = default)
    {
        try
        {
            var encoding = Request.Headers.ContentEncoding.ToString();
            await using var stream = ChunkDecompression.Wrap(Request.Body, encoding);
            await _storage.SaveChunkAsync(uploadId, index, stream, ct);
            await _service.MarkChunkReceivedAsync(uploadId, index, ct);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(new { error = $"Invalid compressed chunk: {ex.Message}" });
        }
    }

    /// <summary>
    /// Finalize the upload: merge chunks, optionally verify checksum, mark Completed.
    /// </summary>
    [HttpPost("{uploadId:guid}/complete")]
    public async Task<IActionResult> Complete(
        [FromRoute] Guid uploadId,
        [FromForm] string? checksum = null,
        CancellationToken ct = default)
    {
        if (checksum is null && Request.HasJsonContentType())
        {
            try
            {
                var body = await Request.ReadFromJsonAsync<CompleteRequest>(cancellationToken: ct);
                checksum = body?.Checksum;
            }
            catch
            {
                // ignore
            }
        }

        try
        {
            var finalPath = await _service.CompleteAsync(uploadId, checksum, ct);
            return Ok(new { path = finalPath });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Abort an in-progress upload and delete temp data.
    /// </summary>
    [HttpDelete("{uploadId:guid}")]
    public async Task<IActionResult> Abort(
        [FromRoute] Guid uploadId,
        CancellationToken ct = default)
    {
        try
        {
            await _service.AbortAsync(uploadId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get current status and list of received chunks (for resume).
    /// Uses filesystem as source of truth so parallel uploads don't under-report.
    /// </summary>
    [HttpGet("{uploadId:guid}/status")]
    public async Task<IActionResult> Status(
        [FromRoute] Guid uploadId,
        CancellationToken ct = default)
    {
        var session = await _service.GetStatusAsync(uploadId, ct);
        if (session is null)
            return NotFound();

        // Disk is authoritative under concurrent parallel chunk uploads.
        var onDisk = await _storage.GetExistingChunkIndexesAsync(uploadId, ct);
        var received = onDisk.OrderBy(x => x).ToArray();

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
            session.Checksum,
            session.CreatedAt,
            session.CompletedAt,
            session.ExpiresAt,
            isExpired = session.IsExpired()
        });
    }

    private sealed class CompleteRequest
    {
        public string? Checksum { get; set; }
    }
}
