namespace OpenCsms.Suite.Support;

using System.Collections.Concurrent;
using System.Text.Json;
using OpenCsms.Messaging;
using ProtoTest.Core;
using ProtoTest.Messaging.RabbitMq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

/// <summary>
/// A raw RabbitMQ client for the assertions the ProtoTest messaging tap cannot express: publishing to
/// the product's <c>(exchange, routing key)</c> pair (the framework's <see cref="ProtoMessage"/> drops
/// the routing key) and reading a queue instead of an exchange (the product's dead-letter target is a
/// queue; REF-5). One connection per test, disposed with it, which returns anything left
/// unacknowledged to its queue.
/// </summary>
public sealed class RabbitMqRawClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnection _connection;
    private readonly IChannel _channel;

    private RabbitMqRawClient(IConnection connection, IChannel channel)
    {
        _connection = connection;
        _channel = channel;
    }

    /// <summary>
    /// Connects to the broker this run uses, resolving the address the way the framework's tap does:
    /// an explicitly configured connection string wins, then the one a started container published.
    /// The product's own keys are accepted too, so the helper cannot address a different broker than
    /// the worker under test.
    /// </summary>
    public static async ValueTask<RabbitMqRawClient> ConnectAsync(
        ProtoExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var connectionString = ResolveConnectionString(context);
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        var connection = await factory.CreateConnectionAsync(cancellationToken);
        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await RabbitMqTopology.EnsureAsync(channel, cancellationToken);
        return new RabbitMqRawClient(connection, channel);
    }

    /// <summary>Publishes a JSON message to an exchange and routing key, persistently.</summary>
    public async ValueTask PublishAsync<T>(
        string exchange,
        string routingKey,
        T message,
        CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent
        };
        await _channel.BasicPublishAsync(
            exchange,
            routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Consumes <paramref name="queue"/> until a delivery matches <paramref name="predicate"/> or
    /// <paramref name="timeout"/> elapses, the bounded wait the suite uses everywhere. The matching
    /// delivery is acknowledged; anything else stays unacknowledged, so disposing the client returns
    /// it to the queue for another run to find.
    /// </summary>
    public async ValueTask<RabbitMqRawDelivery> AwaitAsync(
        string queue,
        Func<RabbitMqRawDelivery, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);
        ArgumentNullException.ThrowIfNull(predicate);

        var received = new ConcurrentQueue<RabbitMqRawDelivery>();
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, args) =>
        {
            received.Enqueue(new RabbitMqRawDelivery(
                args.DeliveryTag,
                args.Body.ToArray(),
                args.RoutingKey,
                args.BasicProperties));
            return Task.CompletedTask;
        };

        var consumerTag = await _channel.BasicConsumeAsync(
            queue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);
        try
        {
            var result = await ProtoPolling.PollAsync(
                _ =>
                {
                    received.TryDequeue(out var delivery);
                    return ValueTask.FromResult(delivery);
                },
                delivery => delivery is not null && predicate(delivery),
                timeout,
                TimeSpan.FromMilliseconds(100),
                cancellationToken);
            if (!result.Satisfied || result.Value is null)
            {
                throw new TimeoutException(
                    $"No message matching the predicate arrived on '{queue}' within {timeout}.");
            }

            await _channel.BasicAckAsync(result.Value.DeliveryTag, multiple: false, cancellationToken);
            return result.Value;
        }
        finally
        {
            try
            {
                await _channel.BasicCancelAsync(consumerTag, cancellationToken: CancellationToken.None);
            }
            catch (AlreadyClosedException)
            {
                // The connection died under the poll; cancelling has nothing left to do, and the
                // real failure (for example the timeout above) must survive teardown (R1a-15).
            }
        }
    }

    /// <summary>
    /// Resolves the broker address this run uses, the same way <see cref="ConnectAsync"/> does, for a
    /// test that must hand the address to a product component instead of a raw client.
    /// </summary>
    public static string ResolveConnectionString(ProtoExecutionContext context)
    {
        var configured = context.Configuration[RabbitMqOptions.ConnectionStringSetting]
            ?? context.Configuration["Messaging:RabbitMq:ConnectionString"]
            ?? context.Configuration["ConnectionStrings:RabbitMq"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (context.TryService<ProtoInfrastructureSettings>() is { } settings)
        {
            if (settings.Values.TryGetValue(RabbitMqOptions.ConnectionStringSetting, out var provided)
                || settings.Values.TryGetValue("Messaging:RabbitMq:ConnectionString", out provided))
            {
                return provided;
            }
        }

        throw new InvalidOperationException(
            $"No broker address is available. Set '{RabbitMqOptions.ConnectionStringSetting}' " +
            "(a started RabbitMQ container does this), or provide the product's " +
            "'Messaging:RabbitMq:ConnectionString'.");
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

/// <summary>One raw delivery: the payload, the wire properties and the tag that acknowledges it.</summary>
public sealed record RabbitMqRawDelivery(
    ulong DeliveryTag,
    ReadOnlyMemory<byte> Body,
    string RoutingKey,
    IReadOnlyBasicProperties Properties);
