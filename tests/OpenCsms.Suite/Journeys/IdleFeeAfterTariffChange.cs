namespace OpenCsms.Suite.Journeys;

using System.Net;
using OpenCsms.Contracts;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Messaging;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using BillingWorker = OpenCsms.Billing.Worker.Program;

/// <summary>
/// A charging session keeps the prices it started under: an unplug five hours after the last meter
/// value is billed the idle fee the tariff promised then, even when the operator reprices the tariff
/// while the car is still plugged in. This is the showpiece journey: it once recorded a real
/// regression, when billing read the tariff at the moment the worker processed the end event, so a
/// repricing that removed the idle fee silently removed the charge for time already spent. The
/// failing trace from then is the evidence the docs link to; the product now copies the tariff onto
/// the session when it starts, and this journey pins that.
/// </summary>
[Application(CsmsTargets.Api)]
[Auth<CsmsMachineKeyAuthenticator>]
[CsmsOperator]
[Category("Showpiece")]
public sealed class IdleFeeAfterTariffChange
{
    [ProtoTest]
    [RequiresTestClock]
    [RequiresCapability(
        ProtoCapabilityKinds.Broker,
        Reason = "The journey awaits invoice.issued on the broker; configure ProtoTest:Messaging:RabbitMq:ConnectionString.")]
    [RequiresWorker<BillingWorker>]
    public async Task TheIdleFeeStillAppliesAfterAReprice()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();

        // The car charges 22 kWh, then stays plugged in; a reprice lands before the unplug.
        Guid sessionId;
        using (var started = await Proto.Context.Rest()
                   .Body(new { stationId = op.StationId, connectorId = 1 })
                   .PostAsync("/api/sessions"))
        {
            started.Should.HaveHttpStatus(HttpStatusCode.Created);
            sessionId = started.ReadAsJson<SessionResponse>()!.Id;
        }

        using (var meter = await Proto.Context.Rest()
                   .Body(new { totalKwh = 22.0m })
                   .PostAsync($"/api/sessions/{sessionId}/meter-values"))
        {
            meter.Should.HaveHttpStatus(HttpStatusCode.OK);
        }

        Proto.Context.Clock.Advance(TimeSpan.FromHours(5));

        // The operator drops the idle fee while the session is open. The prices a session bills are
        // the ones it started under, so the invoice must keep the fee this session accrued.
        await DashboardSession.SignInAsync(op.LoginEmail, op.Password);
        var token = await DashboardSession.AntiforgeryTokenAsync();
        using (var repriced = await Proto.Context.Rest()
                   .Header("X-XSRF-TOKEN", token)
                   .Body(new
                   {
                       energyPricePerKwh = 0.55m,
                       startFee = 1.50m,
                       idleFeePerHour = 0.00m,
                       idleGracePeriod = TimeSpan.FromMinutes(10)
                   })
                   .PutAsync($"/api/dashboard/tariffs/{op.TariffId}"))
        {
            repriced.Should.HaveHttpStatus(HttpStatusCode.OK);
        }

        using (var ended = await Proto.Context.Rest().PostAsync($"/api/sessions/{sessionId}/end"))
        {
            ended.Should.HaveHttpStatus(HttpStatusCode.OK);
        }

        var message = await Proto.Context.Messaging().AwaitAsync(
            CsmsEvents.Exchange,
            candidate => CsmsMessages.IsInvoiceIssuedFor(candidate, sessionId),
            TimeSpan.FromSeconds(30));
        Assert.That(message.ReadRequired<InvoiceIssued>().SessionId, Is.EqualTo(sessionId));

        using var invoice = await Proto.Context.Rest().GetAsync($"/api/sessions/{sessionId}/invoice");
        invoice.Should.HaveHttpStatus(HttpStatusCode.OK);
        var stored = invoice.ReadAsJson<InvoiceResponse>()!;

        Assert.Multiple(() =>
        {
            Assert.That(
                stored.IdleHours,
                Is.EqualTo(5),
                "five started idle hours past the ten-minute grace");
            Assert.That(
                stored.IdleFeeAmount,
                Is.EqualTo(10.00m),
                "the idle fee the session started under still applies");
            Assert.That(
                stored.Total,
                Is.EqualTo(20.30m),
                "22 kWh at the tariff the session started under, the start fee and the idle fee");
        });
    }
}
