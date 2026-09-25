namespace OpenCsms.Suite.Journeys;

using System.Net;
using System.Text.Json;
using OpenCsms.Api;
using OpenCsms.Contracts;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.NUnit;
using ProtoTest.Rest;

/// <summary>
/// The M1 journey: the operator provisioned by <see cref="CsmsOperatorAttribute"/> charges a session,
/// and the invoice arrives as the billing worker's asynchronous <c>invoice.issued</c> event on the
/// product's exchange; the REST read afterwards is the durable side-check. The API answers before the
/// invoice exists, so the suite waits for the event the way a client would.
/// </summary>
[Application("Csms")]
[CsmsOperator]
[RequiresCapability(
    ProtoCapabilityKinds.Broker,
    Reason = "The journey awaits invoice.issued on the broker; configure ProtoTest:Messaging:RabbitMq:ConnectionString.")]
[RequiresCapability(
    ProtoCapabilityKinds.Worker,
    Reason = "The invoice is produced by the billing worker; the run must compose AddWorkerHost<BillingWorker>.")]
public sealed class ChargingSessionsBecomeInvoices
{
    private static readonly TimeSpan InvoiceTimeout = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions InvoiceJson = new(JsonSerializerDefaults.Web);

    [ProtoTest]
    public async Task AChargingSessionBecomesAnInvoice()
    {
        // Arrange: the attribute registered the tenant, tariff and station this test bills against.
        var op = Proto.Context.Resolve<CsmsOperator>();

        // Act: a driver charges 22 kWh on connector 1 and unplugs.
        var started = await StartSessionAsync(op.StationId, connectorId: 1);
        using (var meter = await Proto.Context.Rest()
                   .Body(new { totalKwh = 22.0m })
                   .PostAsync($"/api/sessions/{started.Id}/meter-values"))
        {
            meter.Should.HaveHttpStatus(HttpStatusCode.OK);
        }

        using (var ended = await Proto.Context.Rest().PostAsync($"/api/sessions/{started.Id}/end"))
        {
            ended
                .Should.HaveHttpStatus(HttpStatusCode.OK)
                .ShouldMatchShape(new { id = started.Id, isOpen = false });
        }

        // Assert: the worker published invoice.issued for this session. The tap sees every message on
        // the exchange, so the predicate names both the session and the event.
        var message = await Proto.Context.Messaging().AwaitAsync(
            CsmsEvents.Exchange,
            candidate => IsInvoiceIssuedFor(candidate, started.Id),
            InvoiceTimeout);
        message.ShouldMatchShape(new
        {
            sessionId = started.Id,
            tenantId = op.TenantId,
            total = 10.30m,
            currency = "EUR"
        });
        var issued = JsonSerializer.Deserialize<InvoiceIssued>(message.Payload!, InvoiceJson)!;

        // Durable side-check: the invoice the worker stored is still readable over the API afterwards.
        using var invoiceResponse = await Proto.Context.Rest().GetAsync($"/api/sessions/{started.Id}/invoice");
        invoiceResponse.Should.HaveHttpStatus(HttpStatusCode.OK);
        var invoice = invoiceResponse.ReadAsJson<InvoiceResponse>()!;

        Assert.Multiple(() =>
        {
            Assert.That(issued.SessionId, Is.EqualTo(started.Id));
            Assert.That(issued.TenantId, Is.EqualTo(op.TenantId));
            Assert.That(issued.Total, Is.EqualTo(10.30m));
            Assert.That(invoice.SessionId, Is.EqualTo(started.Id));
            Assert.That(invoice.TenantId, Is.EqualTo(op.TenantId));
            Assert.That(invoice.EnergyKwh, Is.EqualTo(22m));
            Assert.That(invoice.EnergyAmount, Is.EqualTo(8.80m));
            Assert.That(invoice.StartFeeAmount, Is.EqualTo(1.50m));
            Assert.That(invoice.IdleHours, Is.Zero, "the unplug followed the last meter value");
            Assert.That(invoice.Total, Is.EqualTo(10.30m));
            Assert.That(invoice.Currency, Is.EqualTo("EUR"));
        });
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
            var issued = JsonSerializer.Deserialize<InvoiceIssued>(message.Payload, InvoiceJson);
            return issued is { } invoice && invoice.SessionId == sessionId && invoice.InvoiceId != Guid.Empty;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
