using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WebApi.Domain.Events;
using WebApi.Interfaces;

namespace WebApi.Events.Handlers;

public sealed class WebhookOptions
{
    public const string SectionName = "Webhook";

    /// <summary>If empty, handler is a no-op.</summary>
    public string? Url { get; set; }

    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>
/// Bridges lifecycle events to an external HTTP endpoint (API Gateway, orchestrator, …).
/// Enable by setting Webhook:Url in configuration.
/// </summary>
public sealed class WebhookUploadEventHandler : IUploadEventHandler
{
    private readonly HttpClient _http;
    private readonly WebhookOptions _options;
    private readonly ILogger<WebhookUploadEventHandler> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WebhookUploadEventHandler(
        HttpClient http,
        IOptions<WebhookOptions> options,
        ILogger<WebhookUploadEventHandler> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        if (_options.TimeoutSeconds > 0)
            _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public Task HandleCompletedAsync(UploadCompletedEvent @event, CancellationToken ct = default)
        => PostAsync("upload.completed", @event, ct);

    public Task HandleAbortedAsync(UploadAbortedEvent @event, CancellationToken ct = default)
        => PostAsync("upload.aborted", @event, ct);

    public Task HandleFailedAsync(UploadFailedEvent @event, CancellationToken ct = default)
        => PostAsync("upload.failed", @event, ct);

    private async Task PostAsync(string type, object payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.Url))
            return;

        var body = new { type, occurredAt = DateTime.UtcNow, data = payload };

        try
        {
            using var response = await _http.PostAsJsonAsync(_options.Url, body, JsonOptions, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Webhook {Type} returned {StatusCode}", type, (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Webhook {Type} failed", type);
        }
    }
}
