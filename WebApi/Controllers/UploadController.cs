using System.IO.Hashing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WebApi.Infrastructure;
using WebApi.Interfaces;
using WebApi.Storages;

namespace WebApi.Controllers;

[ApiController]
[Route("api/uploads")]
public class UploadController : ControllerBase
{
    private readonly IUploadService _service;
    private readonly IFileStorage _storage;
    private readonly StorageOptions _options;

    public UploadController(
        IUploadService service,
        IFileStorage storage,
        IOptions<StorageOptions> options)
    {
        _service = service;
        _storage = storage;
        _options = options.Value;
    }

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
                expiresAt = session.ExpiresAt,
                requireChunkCrc32 = _options.RequireChunkCrc32
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
    /// Upload a single chunk. Optional Content-Encoding: gzip | deflate | br.
    /// Optional header X-Chunk-CRC32 (hex) for early rejection.
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
            // Validate before any disk write — avoids orphan parts.
            await _service.EnsureCanAcceptChunkAsync(uploadId, index, ct);

            var encoding = Request.Headers.ContentEncoding.ToString();
            await using var decoded = ChunkDecompression.Wrap(Request.Body, encoding);

            var expectedCrc = Request.Headers["X-Chunk-CRC32"].ToString();
            if (_options.RequireChunkCrc32 && string.IsNullOrWhiteSpace(expectedCrc))
                return BadRequest(new { error = "X-Chunk-CRC32 header is required." });

            if (!string.IsNullOrWhiteSpace(expectedCrc))
            {
                var crc = new Crc32();
                await using var tee = new TeeReadStream(decoded, mem => crc.Append(mem.Span));
                await _storage.SaveChunkAsync(uploadId, index, tee, ct);

                var actual = Convert.ToHexString(crc.GetCurrentHash()).ToLowerInvariant();
                if (!ChunkCrc32.EqualsHex(expectedCrc, actual))
                {
                    // Best-effort: leave part; client should retry same index.
                    return BadRequest(new
                    {
                        error = $"Chunk CRC32 mismatch. Expected {expectedCrc}, got {actual}."
                    });
                }
            }
            else
            {
                await _storage.SaveChunkAsync(uploadId, index, decoded, ct);
            }

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

    [HttpGet("{uploadId:guid}/status")]
    public async Task<IActionResult> Status(
        [FromRoute] Guid uploadId,
        CancellationToken ct = default)
    {
        var session = await _service.GetStatusAsync(uploadId, ct);
        if (session is null)
            return NotFound();

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
