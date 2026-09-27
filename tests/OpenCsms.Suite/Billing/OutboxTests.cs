namespace OpenCsms.Suite.Billing;

using System.Net;
using System.Text.Json;
using OpenCsms.Application.Ports;
using OpenCsms.Contracts;
using OpenCsms.Suite.Support;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Messaging;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using BillingWorker = OpenCsms.Billing.Worker.Program;

/// <summary>
/// The session-end outbox under a publish failure: the end request commits the ended session and its
/// <c>session.ended</c> event, the substituted publisher fails the attempt that request makes, and the
/// event is still delivered - the stored row is retried by the dispatcher - while the worker stores
/// exactly one invoice. The substituted publisher delegates every later attempt to the product's own
/// RabbitMQ publisher, so the retry travels the real broker path.
/// </summary>
[Application(CsmsTargets.Api)]
[CsmsOperator]
[Auth<CsmsMachineKeyAuthenticator>]
[RequiresInProcess]
[RequiresCapability(
    ProtoCapabilityKinds.Broker,
    Reason = "The retried event travels the product exchange; configure the broker.")]
[RequiresWorker<BillingWorker>]
public sealed class OutboxTests
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(30);

    [ProtoTest]
    public async Task AFailedPublishIsRetriedByTheDispatcherAndBillsOnce()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();

        // Substitute the API's publisher for this test: the request's first attempt will fail.
        var publisher = new FirstAttemptFailsEventPublisher(
            RabbitMqRawClient.ResolveConnectionString(Proto.Context));
        Proto.Context.Override<IEventPublisher>(publisher);

        var started = await StartSessionAsync(op.StationId, connectorId: 1);
        using (var meter = await Proto.Context.Rest()
                   .Body(new { totalKwh = 22.0m })
                   .PostAsync($"/api/sessions/{started.Id}/meter-values"))
        {
            meter.Should.HaveHttpStatus(HttpStatusCode.OK);
        }

        // Act: the end commits the session and its event, then its publish attempt fails.
        SessionResponse ended;
        using (var response = await Proto.Context.Rest().PostAsync($"/api/sessions/{started.Id}/end"))
        {
            response.Should.HaveHttpStatus(HttpStatusCode.OK);
            ended = response.ReadAsJson<SessionResponse>()!;
        }

        // Assert: the failure did not roll the end back, and the event is still in the store.
        Assert.Multiple(() =>
        {
            Assert.That(ended.EndedAtUtc, Is.Not.Null, "the end committed even though its publish failed");
            Assert.That(publisher.Failures, Is.EqualTo(1), "the publisher rejected the request's own attempt");
        });
        Assert.That(
            await CsmsDatabase.CountPendingOutboxAsync(Proto.Context, started.Id),
            Is.EqualTo(1),
            "the event is stored and still pending a delivery");

        // Assert: the dispatcher retried the row, the worker billed once, and the row is sent.
        var issued = await AwaitInvoiceIssuedAsync(started.Id);
        var stored = await ReadInvoiceAsync(started.Id);
        var invoiceRows = await CsmsDatabase.CountInvoicesAsync(Proto.Context, started.Id);
        var pending = await AwaitOutboxEmptyAsync(started.Id);

        Assert.Multiple(() =>
        {
            Assert.That(issued.InvoiceId, Is.EqualTo(stored.Id), "the delivered event belongs to the stored invoice");
            Assert.That(invoiceRows, Is.EqualTo(1), "the failed attempt and its retry must bill the session once");
            Assert.That(pending, Is.EqualTo(0), "the dispatcher marked the delivered row as sent");
        });
    }

    private static async Task<int> AwaitOutboxEmptyAsync(Guid sessionId)
    {
        var result = await ProtoPolling.PollAsync(
            cancellationToken => new ValueTask<int>(
                CsmsDatabase.CountPendingOutboxAsync(Proto.Context, sessionId, cancellationToken)),
            pending => pending == 0,
            DeliveryTimeout,
            TimeSpan.FromMilliseconds(100),
            CancellationToken.None);
        return result.Value;
    }

    private static async Task<InvoiceIssued> AwaitInvoiceIssuedAsync(Guid sessionId)
    {
        var message = await Proto.Context.Messaging().AwaitAsync(
            CsmsEvents.Exchange,
            candidate => IsInvoiceIssuedFor(candidate, sessionId),
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

    /// <summary>
    /// The tap receives every routing key on the exchange, so the match names the invoice event by
    /// requiring an invoice id as well as this session id.
    /// </summary>
    private static bool IsInvoiceIssuedFor(ProtoMessage message, Guid sessionId)
    {
        if (message.Payload is null || !message.Payload.Contains(sessionId.ToString(), StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var issued = message.ReadAsJson<InvoiceIssued>();
            return issued is { } invoice && invoice.SessionId == sessionId && invoice.InvoiceId != Guid.Empty;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
