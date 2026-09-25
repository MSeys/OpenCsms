namespace OpenCsms.Suite.Billing;

using System.Text;
using System.Text.Json;
using OpenCsms.Contracts;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.NUnit;
using RabbitMQ.Client;
using BillingWorker = OpenCsms.Billing.Worker.Program;

/// <summary>
/// The billing worker's negative path: a <c>session.ended</c> naming a session the store does not know
/// cannot be billed. The worker retries it in-process, then nacks it through the queue's dead-letter
/// exchange; the API can only publish for sessions it knows, so this test publishes the poison itself
/// on the product's exchange and routing key and asserts the delivery on the product's dead-letter
/// queue. It touches no REST client and no fixed operator, so it runs alongside the journey.
/// </summary>
[RequiresCapability(
    ProtoCapabilityKinds.Broker,
    Reason = "The poison travels the product's exchange and dead-letter queue; configure the broker.")]
[RequiresWorker<BillingWorker>]
public sealed class DeadLetterTests
{
    private const string RetriesHeader = "x-opencsms-retries";

    /// <summary>Completed retries before the fourth attempt is dead-lettered.</summary>
    private const int MaxRetries = 3;
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [ProtoTest]
    public async Task AnUnbillableSessionEndedIsDeadLettered()
    {
        // Arrange: a session id no tenant owns, so every billing attempt fails in the store lookup.
        var sessionId = Guid.NewGuid();
        var ended = new SessionEnded(
            sessionId,
            "dlq-operator",
            Guid.NewGuid(),
            ConnectorId: 1,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow,
            EnergyKwh: 1.5m);

        // Act: publish on the product's exchange with the product's routing key, bypassing the API.
        await using var broker = await RabbitMqRawClient.ConnectAsync(Proto.Context);
        await broker.PublishAsync(CsmsEvents.Exchange, CsmsEvents.SessionEndedRoutingKey, ended);

        // Assert: the worker gave up after its retries and dead-lettered this message, not another run's.
        var delivery = await broker.AwaitAsync(
            CsmsEvents.BillingDeadLetterQueue,
            candidate => IsFor(candidate, sessionId),
            DeliveryTimeout);
        var payload = JsonSerializer.Deserialize<SessionEnded>(delivery.Body.Span, Json);
        var retries = ReadRetries(delivery.Properties);

        Assert.Multiple(() =>
        {
            Assert.That(
                payload?.SessionId,
                Is.EqualTo(sessionId),
                "the dead-lettered message is the poison this test published");
            Assert.That(
                retries,
                Is.EqualTo(MaxRetries),
                $"the dead-lettered message records {MaxRetries} completed retries ('{RetriesHeader}'), " +
                $"so it was attempted {MaxRetries + 1} times");
        });
    }

    private static bool IsFor(RabbitMqRawDelivery delivery, Guid sessionId)
    {
        try
        {
            return JsonSerializer.Deserialize<SessionEnded>(delivery.Body.Span, Json)?.SessionId == sessionId;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int? ReadRetries(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is null || !properties.Headers.TryGetValue(RetriesHeader, out var value))
        {
            return null;
        }

        return value switch
        {
            int number => number,
            long number => (int)number,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            _ => null
        };
    }
}
