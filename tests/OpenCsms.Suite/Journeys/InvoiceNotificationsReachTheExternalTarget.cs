namespace OpenCsms.Suite.Journeys;

using System.Net;
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
using BillingWorker = OpenCsms.Billing.Worker.Program;
using NotificationWorker = OpenCsms.Notification.Worker.Program;

/// <summary>
/// The notification path end to end: the billing worker issues an invoice, the notification worker
/// consumes <c>invoice.issued</c> and pushes it to the external invoice-ready target (a real HTTP
/// call to the WireMock fake standing in for the PSP or email relay), and a session the billing
/// worker gave up on reaches the operator's alerting target as a <c>billing.failed</c> notification.
/// The fakes are per-run, so a test names its own invoice or session in the request path and reads
/// exactly that request out of the shared log.
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
public sealed class InvoiceNotificationsReachTheExternalTarget
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(30);

    [ProtoTest]
    [RequiresTestClock]
    public async Task AnIssuedInvoiceReachesTheInvoiceReadyTarget()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        var target = Proto.Context.WireMock(CsmsTargets.InvoiceReadyTarget);
        NotificationTargetStubs.Accept(target, NotificationSettings.InvoiceReadyPath);

        // Act: charge and end a session, exactly like a real customer would.
        var started = await StartSessionAsync(op.StationId, connectorId: 1);
        await RecordMeterAsync(started.Id, 22m);
        await EndSessionAsync(started.Id);

        // Assert: the invoice the billing worker published reached the target, at this invoice's
        // path, with the invoice's own data.
        var issued = await AwaitInvoiceIssuedAsync(started.Id);
        var path = $"{NotificationSettings.InvoiceReadyPath}/{issued.InvoiceId}";
        var received = await AwaitRequestAsync(target, path);
        var payload = ReadRequestPayload(target, path);

        Assert.Multiple(() =>
        {
            Assert.That(received.Method, Is.EqualTo("POST"));
            Assert.That(received.Matched, Is.True, "the notification matched the invoice-ready route");
            Assert.That(
                received.StatusCode,
                Is.EqualTo((int)HttpStatusCode.Accepted),
                "the fake accepted the notification, so the worker counted the delivery as done");
            Assert.That(payload.GetProperty("event").GetString(), Is.EqualTo("invoice-ready"));
            Assert.That(payload.GetProperty("invoiceId").GetGuid(), Is.EqualTo(issued.InvoiceId));
            Assert.That(payload.GetProperty("sessionId").GetGuid(), Is.EqualTo(started.Id));
            Assert.That(payload.GetProperty("tenantId").GetString(), Is.EqualTo(op.TenantId));
            Assert.That(payload.GetProperty("total").GetDecimal(), Is.EqualTo(issued.Total));
            Assert.That(payload.GetProperty("currency").GetString(), Is.EqualTo(issued.Currency));
            Assert.That(
                payload.GetProperty("issuedAtUtc").GetDateTimeOffset(),
                Is.EqualTo(SuiteClock.Instant),
                "the notification carries the invoice's instant, which the worker stamped with the run clock");
        });
    }

    [ProtoTest]
    [RequiresTestClock]
    public async Task ABillingFailureReachesTheAlertingTarget()
    {
        var target = Proto.Context.WireMock(CsmsTargets.BillingFailureTarget);
        NotificationTargetStubs.Accept(target, NotificationSettings.BillingFailurePath);

        // Arrange: a session.ended naming a session the store does not know - the API can only
        // publish for sessions it knows, so the test publishes the poison on the product's exchange
        // and routing key. The billing worker retries it, dead-letters it, and reports the failure.
        var sessionId = Guid.NewGuid();
        var ended = new SessionEnded(
            sessionId,
            "notification-journey",
            Guid.NewGuid(),
            ConnectorId: 1,
            SuiteClock.Instant.AddMinutes(-5),
            SuiteClock.Instant,
            EnergyKwh: 1.5m);
        await using (var broker = await RabbitMqRawClient.ConnectAsync(Proto.Context))
        {
            await broker.PublishAsync(CsmsEvents.Exchange, CsmsEvents.SessionEndedRoutingKey, ended);
        }

        // Assert: the failure reached the alerting target, at this session's path, with the reason
        // the billing worker recorded.
        var path = $"{NotificationSettings.BillingFailurePath}/{sessionId}";
        var received = await AwaitRequestAsync(target, path);
        var payload = ReadRequestPayload(target, path);

        Assert.Multiple(() =>
        {
            Assert.That(received.Method, Is.EqualTo("POST"));
            Assert.That(received.Matched, Is.True, "the notification matched the billing-failure route");
            Assert.That(received.StatusCode, Is.EqualTo((int)HttpStatusCode.Accepted));
            Assert.That(payload.GetProperty("event").GetString(), Is.EqualTo("billing-failed"));
            Assert.That(payload.GetProperty("sessionId").GetGuid(), Is.EqualTo(sessionId));
            Assert.That(payload.GetProperty("tenantId").GetString(), Is.EqualTo("notification-journey"));
            Assert.That(
                payload.GetProperty("reason").GetString(),
                Does.Contain(sessionId.ToString()),
                "the reason names the session the billing worker could not find");
            Assert.That(payload.GetProperty("failedAtUtc").GetDateTimeOffset(), Is.EqualTo(SuiteClock.Instant));
        });
    }

    /// <summary>Waits for the fake to have served a request at the exact path, and returns it.</summary>
    private static async Task<ProtoWireMockRequest> AwaitRequestAsync(ProtoWireMockClient target, string path)
    {
        var result = await ProtoPolling.PollAsync(
            _ => ValueTask.FromResult(target.ReceivedRequests.FirstOrDefault(request => request.Path == path)),
            request => request is not null,
            DeliveryTimeout,
            TimeSpan.FromMilliseconds(100),
            CancellationToken.None);
        if (!result.Satisfied || result.Value is null)
        {
            var seen = string.Join(
                ", ",
                target.ReceivedRequests.Select(request => $"{request.Method} {request.Path}"));
            throw new TimeoutException(
                $"No request for '{path}' reached the fake '{target.Name}' within {DeliveryTimeout}. " +
                $"Served: {seen}.");
        }

        return result.Value;
    }

    /// <summary>Reads the JSON body the fake logged for the request at the path.</summary>
    private static JsonElement ReadRequestPayload(ProtoWireMockClient target, string path)
    {
        var entry = target.Server.LogEntries
            .FirstOrDefault(candidate => candidate.RequestMessage?.Path == path)
            ?? throw new InvalidOperationException(
                $"The fake '{target.Name}' has no request log entry for '{path}'.");
        var body = entry.RequestMessage?.Body;
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException(
                $"The request at '{path}' on the fake '{target.Name}' carried no body.");
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static async Task<InvoiceIssued> AwaitInvoiceIssuedAsync(Guid sessionId)
    {
        var message = await Proto.Context.Messaging().AwaitAsync(
            CsmsEvents.Exchange,
            candidate => CsmsMessages.IsInvoiceIssuedFor(candidate, sessionId),
            DeliveryTimeout);
        return message.ReadRequired<InvoiceIssued>();
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
