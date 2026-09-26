using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WebApi.Interfaces;
using WebApi.Options;

namespace WebApi.Storages;

/// <summary>
/// HTTP client for a single owned blob node (scaffold — perf 4.1).
/// Wire protocol matches docs/OWNED-BLOB-NODES.md sketch.
/// </summary>
public sealed class HttpBlobNodeClient : IBlobNodeClient
{
    private readonly HttpClient _http;
    private readonly string _nodeId;

    public HttpBlobNodeClient(HttpClient http, string nodeId, string? serviceToken)
    {
        _http = http;
        _nodeId = nodeId;
        if (!string.IsNullOrWhiteSpace(serviceToken))
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", serviceToken);
    }

    public string NodeId => _nodeId;

    public async Task PutObjectAsync(string key, Stream data, long? contentLength = null, CancellationToken ct = default)
    {
        using var content = new StreamContent(data);
        if (contentLength is not null)
            content.Headers.ContentLength = contentLength;
        var res = await _http.PutAsync($"/v1/objects/{Uri.EscapeDataString(key)}", content, ct)
            .ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
    }

    public async Task<Stream> GetObjectAsync(string key, CancellationToken ct = default)
    {
        var res = await _http.GetAsync($"/v1/objects/{Uri.EscapeDataString(key)}",
                HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        var res = await _http.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, $"/v1/objects/{Uri.EscapeDataString(key)}"), ct)
            .ConfigureAwait(false);
        return res.IsSuccessStatusCode;
    }

    public async Task DeleteObjectAsync(string key, CancellationToken ct = default)
    {
        var res = await _http.DeleteAsync($"/v1/objects/{Uri.EscapeDataString(key)}", ct)
            .ConfigureAwait(false);
        // 404 is fine for idempotent delete
        if (res.StatusCode != System.Net.HttpStatusCode.NotFound)
            res.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken ct = default)
    {
        var res = await _http.GetAsync($"/v1/objects?prefix={Uri.EscapeDataString(prefix)}", ct)
            .ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        var list = await res.Content.ReadFromJsonAsync<List<string>>(cancellationToken: ct)
            .ConfigureAwait(false);
        return list ?? [];
    }

    public async Task<bool> TryComposeAsync(IReadOnlyList<string> partKeys, string finalKey, CancellationToken ct = default)
    {
        var body = new { parts = partKeys, finalKey };
        var res = await _http.PostAsJsonAsync("/v1/compose", body, ct).ConfigureAwait(false);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound ||
            res.StatusCode == System.Net.HttpStatusCode.NotImplemented)
            return false;
        res.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<BlobNodeHealth> GetHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var res = await _http.GetAsync("/v1/health/ready", ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                return new BlobNodeHealth { Ready = false, Detail = res.StatusCode.ToString() };

            await using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;
            return new BlobNodeHealth
            {
                Ready = true,
                FreeBytes = root.TryGetProperty("freeBytes", out var fb) ? fb.GetInt64() : 0,
                Detail = root.TryGetProperty("detail", out var d) ? d.GetString() : null
            };
        }
        catch (Exception ex)
        {
            return new BlobNodeHealth { Ready = false, Detail = ex.Message };
        }
    }
}

/// <summary>
/// Resolves which blob node should own a given upload (placement strategies from D8).
/// </summary>
public interface IBlobNodeResolver
{
    IBlobNodeClient ResolveForUpload(Guid uploadId);
    IReadOnlyList<IBlobNodeClient> All { get; }
}

public sealed class BlobNodeResolver : IBlobNodeResolver
{
    private readonly IReadOnlyList<IBlobNodeClient> _nodes;
    private readonly string _placement;

    public BlobNodeResolver(IEnumerable<IBlobNodeClient> nodes, IOptions<BlobNodeOptions> options)
    {
        _nodes = nodes.ToList();
        _placement = options.Value.Placement ?? "HashUploadId";
        if (_nodes.Count == 0)
            throw new InvalidOperationException("BlobNodes.Enabled requires at least one node.");
    }

    public IReadOnlyList<IBlobNodeClient> All => _nodes;

    public IBlobNodeClient ResolveForUpload(Guid uploadId)
    {
        // HashUploadId (default): stable sticky placement
        var hash = uploadId.GetHashCode();
        if (hash == int.MinValue) hash = 0;
        var idx = Math.Abs(hash) % _nodes.Count;
        return _nodes[idx];
    }
}
