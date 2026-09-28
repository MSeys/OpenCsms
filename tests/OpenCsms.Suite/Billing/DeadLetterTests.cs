namespace OpenCsms.Suite.Billing;

using System.Text.Json;
using OpenCsms.Contracts;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.Json;
using ProtoTest.Messaging;
using ProtoTest.NUnit;
using BillingWorker = OpenCsms.Billing.Worker.Program;

/// <summary>
/// The billing worker's negative path: a <c>session.ended</c> naming a session the store does not know
/// cannot be billed. The worker retries it in-process, then nacks it through the queue's dead-letter
/// exchange; the API can only publish for sessions it knows, so this test publishes the poison itself
/// on the product's exchange and routing key and asserts the dead-lettered delivery through the
/// framework. The dead letters are awaited on the product's dead-letter exchange: the tap binds a
/// test-owned queue to it, and the product's own binding carries the same delivery into the
/// dead-letter queue. It touches no REST client and no fixed operator, so it runs alongside the
/// journey.
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
        await Proto.Context.Messaging().PublishAsync(
            CsmsEvents.Exchange,
            CsmsEvents.SessionEndedRoutingKey,
            JsonSerializer.Serialize(ended, ProtoJsonDefaults.Web));

        // Assert: the worker gave up after its retries and dead-lettered this message, not another run's.
        var delivery = await Proto.Context.Messaging().AwaitAsync(
            CsmsEvents.DeadLetterExchange,
            candidate => CsmsMessages.IsSessionEndedFor(candidate, sessionId),
            DeliveryTimeout);
        var payload = delivery.ReadRequired<SessionEnded>();

        Assert.Multiple(() =>
        {
            Assert.That(
                payload.SessionId,
                Is.EqualTo(sessionId),
                "the dead-lettered message is the poison this test published");
            Assert.That(
                delivery.RoutingKey,
                Is.EqualTo(CsmsEvents.BillingQueue),
                "the worker's retry republishes through the queue, so the dead letter carries the queue's " +
                "routing key - the framework surfaces what the transport delivered");
            Assert.That(
                ReadRetries(delivery),
                Is.EqualTo(MaxRetries),
                $"the dead-lettered message records {MaxRetries} completed retries ('{RetriesHeader}'), " +
                $"so it was attempted {MaxRetries + 1} times");
        });
    }

    private static int? ReadRetries(ProtoMessage delivery)
    {
        if (delivery.Headers is null
            || !delivery.Headers.TryGetValue(RetriesHeader, out var value)
            || value is null)
        {
            return null;
        }

        return int.TryParse(value, out var parsed) ? parsed : null;
    }
}
