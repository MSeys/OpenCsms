namespace OpenCsms.Suite.Journeys;

using System.Net;
using OpenCsms.Contracts;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using BillingWorker = OpenCsms.Billing.Worker.Program;

/// <summary>
/// The charging-to-invoice journey: the operator provisioned by
/// <see cref="CsmsOperatorAttribute"/> charges a session, and the invoice arrives as the billing
/// worker's asynchronous <c>invoice.issued</c> event on the product's exchange; the REST read
/// afterwards is the durable side-check. The API answers before the invoice exists, so the suite
/// waits for the event the way a client would.
/// </summary>
[Application(CsmsTargets.Api)]
[CsmsOperator]
[RequiresCapability(
    ProtoCapabilityKinds.Broker,
    Reason = "The journey awaits invoice.issued on the broker; configure ProtoTest:Messaging:RabbitMq:ConnectionString.")]
[RequiresWorker<BillingWorker>]
public sealed class ChargingSessionsBecomeInvoices
{
    private static readonly TimeSpan InvoiceTimeout = TimeSpan.FromSeconds(30);

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
                .Should.MatchShape(new { id = started.Id, isOpen = false });
        }

        // Assert: the worker published invoice.issued for this session. The tap sees every message on
        // the exchange, so the predicate names both the session and the event.
        var message = await Proto.Context.Messaging().AwaitAsync(
            CsmsEvents.Exchange,
            candidate => CsmsMessages.IsInvoiceIssuedFor(candidate, started.Id),
            InvoiceTimeout);
        message.Should.MatchShape(new
        {
            sessionId = started.Id,
            tenantId = op.TenantId,
            total = 10.30m,
            currency = "EUR"
        });
        var issued = message.ReadRequired<InvoiceIssued>();

        // Durable side-check: the invoice the worker stored is still readable over the API afterwards.
        using var invoiceResponse = await Proto.Context.Rest().GetAsync($"/api/sessions/{started.Id}/invoice");
        invoiceResponse.Should.HaveHttpStatus(HttpStatusCode.OK);
        var invoice = invoiceResponse.ReadAsJson<InvoiceResponse>()!;

        Assert.Multiple(() =>
        {
            // sessionId, tenantId, total and currency are already pinned by the shape assertion above;
            // only the facts it cannot express are asserted again here.
            Assert.That(
                issued.IssuedAtUtc,
                Is.EqualTo(SuiteClock.Instant),
                "the worker stamped the event with the run's injected clock");
            Assert.That(invoice.SessionId, Is.EqualTo(started.Id));
            Assert.That(invoice.TenantId, Is.EqualTo(op.TenantId));
            Assert.That(
                invoice.IssuedAtUtc,
                Is.EqualTo(SuiteClock.Instant),
                "the stored invoice carries the injected clock's instant, not the machine's");
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
}
