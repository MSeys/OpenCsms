namespace OpenCsms.Suite.Billing;

using System.Net;
using System.Text.Json;
using OpenCsms.Api;
using OpenCsms.Contracts;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using BillingWorker = OpenCsms.Billing.Worker.Program;

/// <summary>
/// R1a-01: a redelivered <c>session.ended</c> must republish <c>invoice.issued</c> for the same
/// invoice, and must not store a second invoice. The test runs the journey's setup path once, then
/// publishes the product's own payload on the product's exchange and routing key again and asserts the
/// event arrives a second time with the same invoice id while the store still holds one invoice row.
/// </summary>
[Application(CsmsTargets.Api)]
[CsmsOperator]
[RequiresCapability(
    ProtoCapabilityKinds.Broker,
    Reason = "The redelivery is published and awaited on the product exchange; configure the broker.")]
[RequiresWorker<BillingWorker>]
public sealed class RedeliveryTests
{
    private static readonly TimeSpan InvoiceTimeout = TimeSpan.FromSeconds(30);

    [ProtoTest]
    public async Task ARedeliveredSessionEndedRepublishesTheSameInvoice()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();

        // Arrange: the journey's setup path, so a session and its invoice exist.
        var started = await StartSessionAsync(op.StationId, connectorId: 1);
        using (var meter = await Proto.Context.Rest()
                   .Body(new { totalKwh = 22.0m })
                   .PostAsync($"/api/sessions/{started.Id}/meter-values"))
        {
            meter.Should.HaveHttpStatus(HttpStatusCode.OK);
        }

        SessionResponse ended;
        using (var response = await Proto.Context.Rest().PostAsync($"/api/sessions/{started.Id}/end"))
        {
            response.Should.HaveHttpStatus(HttpStatusCode.OK);
            ended = response.ReadAsJson<SessionResponse>()!;
        }

        var firstIssued = await AwaitInvoiceIssuedAsync(started.Id);
        var firstStored = await ReadInvoiceAsync(started.Id);

        // Act: the product publishes session.ended once; publish the same payload a second time by hand.
        var redelivery = new SessionEnded(
            started.Id,
            op.TenantId,
            op.StationId,
            started.ConnectorId,
            started.StartedAtUtc,
            ended.EndedAtUtc!.Value,
            ended.EnergyKwh);
        await using (var broker = await RabbitMqRawClient.ConnectAsync(Proto.Context))
        {
            await broker.PublishAsync(CsmsEvents.Exchange, CsmsEvents.SessionEndedRoutingKey, redelivery);
        }

        // Assert: the redelivery republishes the stored invoice and stores nothing new.
        var secondIssued = await AwaitInvoiceIssuedAsync(started.Id);
        var secondStored = await ReadInvoiceAsync(started.Id);
        var invoiceRows = await CsmsDatabase.CountInvoicesAsync(Proto.Context, started.Id);

        Assert.Multiple(() =>
        {
            Assert.That(
                secondIssued.InvoiceId,
                Is.EqualTo(firstIssued.InvoiceId),
                "the redelivery republishes the invoice the first delivery stored");
            Assert.That(
                secondStored.Id,
                Is.EqualTo(firstStored.Id),
                "both REST reads see the same invoice row");
            Assert.That(
                invoiceRows,
                Is.EqualTo(1),
                "the redelivery must not insert a second invoice row");
        });
    }

    private static async Task<InvoiceIssued> AwaitInvoiceIssuedAsync(Guid sessionId)
    {
        var message = await Proto.Context.Messaging().AwaitAsync(
            CsmsEvents.Exchange,
            candidate => IsInvoiceIssuedFor(candidate, sessionId),
            InvoiceTimeout);
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
