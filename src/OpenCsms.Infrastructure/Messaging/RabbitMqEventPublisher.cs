namespace OpenCsms.Infrastructure.Messaging;

using System.Text.Json;
using Microsoft.Extensions.Configuration;
using OpenCsms.Application.Ports;
using OpenCsms.Contracts;
using RabbitMQ.Client;

/// <summary>
/// The <see cref="IEventPublisher"/> over RabbitMQ: a lazily connected publisher, so a service starts
/// and answers health checks even when the broker is not reachable yet, and the first publish pays the
/// connection. The connection string is
/// resolved from configuration on first use, so the run's container settings - which arrive after the
/// publisher is registered - are seen; a deployment configures the same key. A channel the broker
/// closed is never cached: a restart or a failed declaration leaves a dead pair that the next publish
/// throws away before connecting again.
/// </summary>
public sealed class RabbitMqEventPublisher : IEventPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConfiguration _configuration;
    private readonly Func<Uri, CancellationToken, Task<IConnection>> _connect;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqEventPublisher(IConfiguration configuration)
        : this(configuration, static (uri, cancellationToken) =>
            new ConnectionFactory { Uri = uri }.CreateConnectionAsync(cancellationToken))
    {
    }

    /// <summary>
    /// The seam a test uses to observe the connection lifecycle: it supplies the connect operation,
    /// counts the connections it creates and can close one to simulate the broker going away.
    /// Production registration uses the single-argument constructor.
    /// </summary>
    internal RabbitMqEventPublisher(
        IConfiguration configuration,
        Func<Uri, CancellationToken, Task<IConnection>> connect)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
    }

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
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_channel is { IsOpen: true })
            {
                return _channel;
            }

            // A non-open channel means the broker restarted or the declaration failed: the old pair is
            // garbage, not a usable connection.
            await ResetAsync();

            var connectionString = _configuration["Messaging:RabbitMq:ConnectionString"]
                ?? _configuration["ConnectionStrings:RabbitMq"];
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "No broker is configured. Set 'Messaging:RabbitMq:ConnectionString' (the suite's infrastructure does this).");
            }

            try
            {
                _connection = await _connect(new Uri(connectionString), cancellationToken);
                _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
                await _channel.ExchangeDeclareAsync(
                    CsmsEvents.Exchange,
                    ExchangeType.Topic,
                    durable: true,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: cancellationToken);
            }
            catch
            {
                // Never cache half a connection: the next publish must retry from scratch.
                await ResetAsync();
                throw;
            }

            return _channel;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask ResetAsync()
    {
        var channel = _channel;
        _channel = null;
        if (channel is not null)
        {
            await channel.DisposeAsync();
        }

        var connection = _connection;
        _connection = null;
        if (connection is not null)
        {
            await connection.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetAsync();
        _gate.Dispose();
    }
}
