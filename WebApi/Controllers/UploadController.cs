using WebApi.Interfaces;
using WebApi.Storages;
using Microsoft.AspNetCore.Mvc;
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

    [HttpPost("initiate")]
    public async Task<IActionResult> Initiate([FromForm] string fileName, [FromForm] long totalSize,
        [FromForm] int chunkSize = 2_000_000)
    {
        var s = await _service.InitiateAsync(fileName, totalSize, chunkSize);
        return Ok(new { uploadId = s.Id, chunkSize = s.ChunkSize, totalChunks = s.TotalChunks });
    }

    [HttpPut("{uploadId}/chunk/{index}")]
    public async Task<IActionResult> UploadChunk([FromRoute] Guid uploadId, [FromRoute] int index)
    {
        await _storage.SaveChunkAsync(uploadId, index, Request.Body, HttpContext.RequestAborted);
        await _service.MarkChunkReceivedAsync(uploadId, index);
        return Ok();
    }

    [HttpPost("{uploadId}/complete")]
    public async Task<IActionResult> Complete([FromRoute] Guid uploadId)
    {
        await _service.MergeChunksAsync(uploadId);
        return Ok();
    }

    [HttpGet("{uploadId}/status")]
    public async Task<IActionResult> Status([FromRoute] Guid uploadId)
    {
        var s = await _service.GetStatusAsync(uploadId);
        if (s == null) return NotFound();
        var received = s.ReceivedChunks.Keys.OrderBy(k => k).ToArray();
        return Ok(new { s.Id, s.FileName, s.TotalSize, s.ChunkSize, s.TotalChunks, received, s.Completed });
    }
}