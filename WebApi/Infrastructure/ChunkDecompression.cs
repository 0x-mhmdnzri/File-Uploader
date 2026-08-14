using System.IO.Compression;

namespace WebApi.Infrastructure;

/// <summary>
/// Optional per-chunk transport compression.
/// Client may send Content-Encoding: gzip | deflate | br on each chunk.
/// Stored parts are always raw (decompressed) so merge stays simple.
/// </summary>
public static class ChunkDecompression
{
    public static Stream Wrap(Stream body, string? contentEncoding)
    {
        if (string.IsNullOrWhiteSpace(contentEncoding))
            return body;

        // Take first token (e.g. "gzip, br" → gzip)
        var encoding = contentEncoding.Split(',')[0].Trim().ToLowerInvariant();

        return encoding switch
        {
            "gzip" or "x-gzip" => new GZipStream(body, CompressionMode.Decompress, leaveOpen: false),
            "deflate" => new DeflateStream(body, CompressionMode.Decompress, leaveOpen: false),
            "br" or "brotli" => new BrotliStream(body, CompressionMode.Decompress, leaveOpen: false),
            "identity" => body,
            _ => throw new InvalidOperationException(
                $"Unsupported Content-Encoding '{encoding}'. Allowed: gzip, deflate, br.")
        };
    }
}
