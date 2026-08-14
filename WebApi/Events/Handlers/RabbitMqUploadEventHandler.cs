using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using WebApi.Domain.Events;
using WebApi.Interfaces;

namespace WebApi.Events.Handlers;

/// <summary>
/// Optional bridge: publish upload lifecycle events to RabbitMQ when RabbitMq:Enabled=true.
/// </summary>
public sealed class RabbitMqUploadEventHandler : IUploadEventHandler, IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqUploadEventHandler> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqUploadEventHandler(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqUploadEventHandler> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task HandleCompletedAsync(UploadCompletedEvent @event, CancellationToken ct = default) =>
        PublishAsync(_options.RoutingKeyCompleted, @event, ct);

    public Task HandleAbortedAsync(UploadAbortedEvent @event, CancellationToken ct = default) =>
        PublishAsync(_options.RoutingKeyAborted, @event, ct);

    public Task HandleFailedAsync(UploadFailedEvent @event, CancellationToken ct = default) =>
        PublishAsync(_options.RoutingKeyFailed, @event, ct);

    private async Task PublishAsync<T>(string routingKey, T payload, CancellationToken ct)
    {
        if (!_options.Enabled)
            return;

        try
        {
            var ch = await EnsureChannelAsync(ct).ConfigureAwait(false);
            var json = JsonSerializer.SerializeToUtf8Bytes(payload);
            var props = new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Persistent };
            await ch.BasicPublishAsync(
                exchange: _options.Exchange,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: props,
                body: json,
                cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RabbitMQ publish failed for {RoutingKey}", routingKey);
        }
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_channel is { IsOpen: true })
                return _channel;

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost
            };

            _connection = await factory.CreateConnectionAsync(ct).ConfigureAwait(false);
            _channel = await _connection.CreateChannelAsync(cancellationToken: ct).ConfigureAwait(false);
            await _channel.ExchangeDeclareAsync(
                _options.Exchange,
                ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: ct).ConfigureAwait(false);

            return _channel;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.CloseAsync().ConfigureAwait(false);
        if (_connection is not null)
            await _connection.CloseAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
