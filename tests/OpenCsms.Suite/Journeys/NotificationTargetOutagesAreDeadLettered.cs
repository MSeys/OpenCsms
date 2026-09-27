namespace OpenCsms.Suite.Journeys;

using System.Net;
using System.Text;
using System.Text.Json;
using OpenCsms.Contracts;
using OpenCsms.Infrastructure.Notifications;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Messaging;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.WireMock;
using RabbitMQ.Client;
using BillingWorker = OpenCsms.Billing.Worker.Program;
using NotificationWorker = OpenCsms.Notification.Worker.Program;

/// <summary>
/// The notification path under a target that stays down: the fake answers 503 for one entity's
/// notifications, the notification worker spends its in-process retries and dead-letters the
/// delivery, and the notification is on its own dead-letter queue with the entity's ids and the
/// completed-retry count. The invoice side of the journey stays intact - the failed notification
/// neither loses nor duplicates the stored invoice - and a billing failure whose alerting target is
/// down is dead-lettered the same way. The per-run fakes are shared with the parallel notification
/// journeys, so each rejection is keyed to the body's session id and leaves every other
/// notification alone.
/// </summary>
[Application(CsmsTargets.Api)]
[CsmsOperator]
[Auth<CsmsMachineKeyAuthenticator>]
[RequiresInProcess]
[RequiresCapability(
    ProtoCapabilityKinds.Broker,
    Reason = "The notification worker consumes the product's events from the broker; configure it.")]
[RequiresWorker<BillingWorker>]
[RequiresWorker<NotificationWorker>]
public sealed class NotificationTargetOutagesAreDeadLettered
{
    private const string RetriesHeader = "x-opencsms-retries";

    /// <summary>Completed retries before the fourth attempt is dead-lettered.</summary>
    private const int MaxRetries = 3;
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [ProtoTest]
    [RequiresTestClock]
    public async Task AnInvoiceReadyNotificationIsDeadLetteredWhenTheTargetStaysDown()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        var target = Proto.Context.WireMock(CsmsTargets.InvoiceReadyTarget);

        // Arrange: a real invoice, and a target that rejects this session's notification only.
        var started = await StartSessionAsync(op.StationId, connectorId: 1);
        await RecordMeterAsync(started.Id, 22m);
        NotificationTargetStubs.Reject(target, NotificationSettings.InvoiceReadyPath, started.Id);
        await EndSessionAsync(started.Id);

        var issued = await AwaitInvoiceIssuedAsync(started.Id);
        var path = $"{NotificationSettings.InvoiceReadyPath}/{issued.InvoiceId}";

        // Assert: the consumer retried the delivery and gave up; the dead letter carries the
        // invoice's ids and the completed-retry count.
        await using var broker = await RabbitMqRawClient.ConnectAsync(Proto.Context);
        var dead = await broker.AwaitAsync(
            CsmsEvents.NotificationInvoiceIssuedDeadLetterQueue,
            candidate => IsInvoiceIssuedFor(candidate, issued.InvoiceId),
            DeliveryTimeout);
        var payload = JsonSerializer.Deserialize<InvoiceIssued>(dead.Body.Span, Json);
        var retries = ReadRetries(dead.Properties);

        var requests = RequestsAt(target, path);
        var stored = await ReadInvoiceAsync(started.Id);
        var invoiceRows = await CsmsDatabase.CountInvoicesAsync(Proto.Context, started.Id);

        Assert.Multiple(() =>
        {
            Assert.That(payload?.InvoiceId, Is.EqualTo(issued.InvoiceId), "the dead letter is this invoice's notification");
            Assert.That(payload?.SessionId, Is.EqualTo(started.Id));
            Assert.That(
                retries,
                Is.EqualTo(MaxRetries),
                $"the dead letter records {MaxRetries} completed retries ('{RetriesHeader}'), " +
                $"so it was attempted {MaxRetries + 1} times");
            Assert.That(requests, Has.Count.EqualTo(MaxRetries + 1), "the target saw every failed attempt");
            Assert.That(
                requests.All(request => request.StatusCode == (int)HttpStatusCode.ServiceUnavailable),
                Is.True,
                "the target rejected every attempt");
            Assert.That(stored.Id, Is.EqualTo(issued.InvoiceId), "the invoice the target never accepted is stored once");
            Assert.That(stored.Total, Is.EqualTo(issued.Total));
            Assert.That(invoiceRows, Is.EqualTo(1), "the failed notification must not disturb the invoice");
        });
    }

    [ProtoTest]
    [RequiresTestClock]
    public async Task ABillingFailureNotificationIsDeadLetteredWhenTheTargetStaysDown()
    {
        var target = Proto.Context.WireMock(CsmsTargets.BillingFailureTarget);

        // Arrange: a session the store does not know, so the billing worker retries it, gives up,
        // and reports the failure; the alerting target rejects the report for this session only.
        var sessionId = Guid.NewGuid();
        var ended = new SessionEnded(
            sessionId,
            "notification-outage",
            Guid.NewGuid(),
            ConnectorId: 1,
            SuiteClock.Instant.AddMinutes(-5),
            SuiteClock.Instant,
            EnergyKwh: 1.5m);
        NotificationTargetStubs.Reject(target, NotificationSettings.BillingFailurePath, sessionId);

        await using (var broker = await RabbitMqRawClient.ConnectAsync(Proto.Context))
        {
            await broker.PublishAsync(CsmsEvents.Exchange, CsmsEvents.SessionEndedRoutingKey, ended);
        }

        // Assert: the report's delivery was retried and dead-lettered with the session's id and the
        // reason the billing worker recorded.
        var path = $"{NotificationSettings.BillingFailurePath}/{sessionId}";
        await using var consumer = await RabbitMqRawClient.ConnectAsync(Proto.Context);
        var dead = await consumer.AwaitAsync(
            CsmsEvents.NotificationBillingFailedDeadLetterQueue,
            candidate => IsBillingFailedFor(candidate, sessionId),
            DeliveryTimeout);
        var payload = JsonSerializer.Deserialize<SessionBillingFailed>(dead.Body.Span, Json);
        var retries = ReadRetries(dead.Properties);
        var requests = RequestsAt(target, path);
        var invoices = await CsmsDatabase.CountInvoicesAsync(Proto.Context, sessionId);

        Assert.Multiple(() =>
        {
            Assert.That(payload?.SessionId, Is.EqualTo(sessionId), "the dead letter is this session's failure report");
            Assert.That(
                payload?.Reason,
                Does.Contain(sessionId.ToString()),
                "the report names the session the billing worker could not find");
            Assert.That(retries, Is.EqualTo(MaxRetries));
            Assert.That(requests, Has.Count.EqualTo(MaxRetries + 1), "the target saw every failed attempt");
            Assert.That(
                requests.All(request => request.StatusCode == (int)HttpStatusCode.ServiceUnavailable),
                Is.True,
                "the target rejected every attempt");
            Assert.That(invoices, Is.Zero, "no invoice was ever issued for the session the worker could not bill");
        });
    }

    /// <summary>The requests the fake served at exactly this path, the attempts the target saw.</summary>
    private static List<ProtoWireMockRequest> RequestsAt(ProtoWireMockClient target, string path)
        => [.. target.ReceivedRequests.Where(request => request.Path == path)];

    private static bool IsInvoiceIssuedFor(RabbitMqRawDelivery delivery, Guid invoiceId)
    {
        try
        {
            return JsonSerializer.Deserialize<InvoiceIssued>(delivery.Body.Span, Json)?.InvoiceId == invoiceId;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsBillingFailedFor(RabbitMqRawDelivery delivery, Guid sessionId)
    {
        try
        {
            return JsonSerializer.Deserialize<SessionBillingFailed>(delivery.Body.Span, Json)?.SessionId == sessionId;
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

    private static async Task<InvoiceIssued> AwaitInvoiceIssuedAsync(Guid sessionId)
    {
        var message = await Proto.Context.Messaging().AwaitAsync(
            CsmsEvents.Exchange,
            candidate => CsmsMessages.IsInvoiceIssuedFor(candidate, sessionId),
            DeliveryTimeout);
        return message.ReadRequired<InvoiceIssued>();
    }

    private static async Task<InvoiceResponse> ReadInvoiceAsync(Guid sessionId)
    {
        using var response = await Proto.Context.Rest().GetAsync($"/api/sessions/{sessionId}/invoice");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        return response.ReadAsJson<InvoiceResponse>()!;
    }

    private static async Task<SessionResponse> StartSessionAsync(Guid stationId, int connectorId)
    {
        using var response = await Proto.Context.Rest()
            .Body(new { stationId, connectorId })
            .PostAsync("/api/sessions");
        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        return response.ReadAsJson<SessionResponse>()!;
    }

    private static async Task RecordMeterAsync(Guid sessionId, decimal totalKwh)
    {
        using var response = await Proto.Context.Rest()
            .Body(new { totalKwh })
            .PostAsync($"/api/sessions/{sessionId}/meter-values");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
    }

    private static async Task EndSessionAsync(Guid sessionId)
    {
        using var response = await Proto.Context.Rest().PostAsync($"/api/sessions/{sessionId}/end");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
    }
}
