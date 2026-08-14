using Microsoft.AspNetCore.Mvc;
using WebApi.Interfaces;
using WebApi.Storages;

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

    private string? GetClientIp()
    {
        // Prefer X-Forwarded-For if behind proxy, otherwise connection remote IP
        var forwarded = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            return forwarded.Split(',')[0].Trim();
        }
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    [HttpPost("initiate")]
    public async Task<IActionResult> Initiate([FromForm] string fileName, [FromForm] long totalSize,
        [FromForm] int chunkSize = 2_000_000)
    {
        try
        {
            var clientIp = GetClientIp();
            var s = await _service.InitiateAsync(fileName, totalSize, chunkSize, clientIp);
            return Ok(new
            {
                uploadId = s.Id,
                chunkSize = s.ChunkSize,
                totalChunks = s.TotalChunks,
                expiresAt = s.ExpiresAt
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Rate limit"))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = ex.Message });
        }
    }

    [HttpPut("{uploadId}/chunk/{index}")]
    public async Task<IActionResult> UploadChunk([FromRoute] Guid uploadId, [FromRoute] int index)
    {
        try
        {
            await _storage.SaveChunkAsync(uploadId, index, Request.Body, HttpContext.RequestAborted);
            await _service.MarkChunkReceivedAsync(uploadId, index);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{uploadId}/complete")]
    public async Task<IActionResult> Complete([FromRoute] Guid uploadId)
    {
        try
        {
            await _service.MergeChunksAsync(uploadId);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{uploadId}/status")]
    public async Task<IActionResult> Status([FromRoute] Guid uploadId)
    {
        var s = await _service.GetStatusAsync(uploadId);
        if (s == null) return NotFound();

        var received = s.ReceivedChunks.Keys.OrderBy(k => k).ToArray();
        return Ok(new
        {
            s.Id,
            s.FileName,
            s.TotalSize,
            s.ChunkSize,
            s.TotalChunks,
            received,
            s.Completed,
            status = s.Status.ToString(),
            expiresAt = s.ExpiresAt,
            clientIp = s.ClientIp
        });
    }
}
