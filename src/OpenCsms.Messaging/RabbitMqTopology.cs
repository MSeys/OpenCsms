namespace OpenCsms.Messaging;

using OpenCsms.Contracts;
using RabbitMQ.Client;

/// <summary>
/// The broker topology both sides share. Declaring it is idempotent, so the API and the worker can each
/// call it on connect in any order.
/// </summary>
public static class RabbitMqTopology
{
    public static async Task EnsureAsync(IChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);

        await channel.ExchangeDeclareAsync(
            CsmsEvents.Exchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        // The dead-letter path is part of the topology, not an afterthought: a message the worker
        // rejects for good lands in the dead-letter queue where an operator can see it.
        await channel.ExchangeDeclareAsync(
            CsmsEvents.DeadLetterExchange,
            ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            CsmsEvents.BillingQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = CsmsEvents.DeadLetterExchange
            },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            CsmsEvents.BillingQueue,
            CsmsEvents.Exchange,
            CsmsEvents.SessionEndedRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            CsmsEvents.BillingDeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            CsmsEvents.BillingDeadLetterQueue,
            CsmsEvents.DeadLetterExchange,
            routingKey: string.Empty,
            arguments: null,
            cancellationToken: cancellationToken);
    }
}
