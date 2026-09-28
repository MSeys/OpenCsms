namespace OpenCsms.Suite.Messaging;

using Microsoft.Extensions.Configuration;
using OpenCsms.Contracts;
using OpenCsms.Infrastructure.Messaging;
using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.Messaging.RabbitMq;
using ProtoTest.NUnit;
using RabbitMQ.Client;

/// <summary>
/// The publisher must not cache a channel the broker closed. The test publishes once, closes
/// the connection out from under the publisher the way a broker restart would, publishes again and
/// asserts the second message arrived over a fresh connection. It owns its connection and routing key,
/// so it runs alongside the suite's other broker tests.
/// </summary>
[RequiresCapability(
    ProtoCapabilityKinds.Broker,
    Reason = "The publisher connects to the run's broker; configure ProtoTest:Messaging:RabbitMq:ConnectionString.")]
public sealed class RabbitMqEventPublisherTests
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(30);

    [ProtoTest]
    public async Task AClosedChannelIsReplacedOnTheNextPublish()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:RabbitMq:ConnectionString"] = Proto.Context.Service<RabbitMqOptions>().ConnectionString
            })
            .Build();
        var connections = new List<IConnection>();
        await using var publisher = new RabbitMqEventPublisher(configuration, async (uri, cancellationToken) =>
        {
            var connection = await new ConnectionFactory { Uri = uri }.CreateConnectionAsync(cancellationToken);
            connections.Add(connection);
            return connection;
        });

        // Arrange: publish through the channel the publisher caches.
        var first = $"probe-{Guid.NewGuid():N}";
        await publisher.PublishAsync("publisher.probe", new { marker = first });

        // Act: the broker goes away; the publisher's cached channel dies with its connection.
        var original = connections.Single();
        await original.CloseAsync();
        Assert.That(original.IsOpen, Is.False, "the simulated outage closed the publisher's connection");

        var second = $"probe-{Guid.NewGuid():N}";
        await publisher.PublishAsync("publisher.probe", new { marker = second });

        // Assert: the second publish opened a new connection and reached the exchange.
        var message = await Proto.Context.Messaging().AwaitAsync(
            CsmsEvents.Exchange,
            candidate => candidate.Payload?.Contains(second, StringComparison.Ordinal) == true,
            DeliveryTimeout);

        Assert.Multiple(() =>
        {
            Assert.That(connections.Count, Is.EqualTo(2), "the closed channel was thrown away, not reused");
            Assert.That(message.Payload, Does.Contain(second), "the recovered publish reached the broker");
        });
    }
}
