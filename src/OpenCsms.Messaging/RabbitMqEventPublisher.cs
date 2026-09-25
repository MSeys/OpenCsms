namespace OpenCsms.Messaging;

using System.Text.Json;
using OpenCsms.Contracts;
using RabbitMQ.Client;

/// <summary>Publishes integration events to the CSMS topic exchange.</summary>
public interface IEventPublisher
{
    ValueTask PublishAsync<T>(string routingKey, T message, CancellationToken cancellationToken = default);
}

/// <summary>
/// A lazily connected RabbitMQ publisher: a service starts and answers health checks even when the
/// broker is not reachable yet, and the first publish pays the connection. The connection string comes
/// from the suite's run settings in-process and from the environment in a deployment.
/// </summary>
public sealed class RabbitMqEventPublisher(string? connectionString) : IEventPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async ValueTask PublishAsync<T>(string routingKey, T message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);
        var channel = await GetChannelAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent
        };
        await channel.BasicPublishAsync(
            CsmsEvents.Exchange,
            routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }

    private async ValueTask<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            return _channel;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "No broker is configured. Set 'Messaging:RabbitMq:ConnectionString' (the suite's infrastructure does this).");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_channel is null)
            {
                var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
                _connection = await factory.CreateConnectionAsync(cancellationToken);
                _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
                await _channel.ExchangeDeclareAsync(
                    CsmsEvents.Exchange,
                    ExchangeType.Topic,
                    durable: true,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }

        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _gate.Dispose();
    }
}
