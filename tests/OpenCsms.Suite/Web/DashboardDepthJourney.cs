namespace OpenCsms.Suite.Web;

using System.Net;
using OpenCsms.Contracts;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.Web;
using ProtoTest.Web.Playwright;
using BillingWorker = OpenCsms.Billing.Worker.Program;

/// <summary>
/// The dashboard depth journey: a real billed session drives the station timeline (connector,
/// start/end, energy, invoice link), the invoice view shows the calculation lines the worker
/// stored, the operator reprices the tariff through the dashboard form, and the remote start
/// against the unconnected charge point answers its honest refusal on the screen.
/// </summary>
[Application(CsmsTargets.Dashboard)]
[CsmsOperator]
[WebSession("Default", Application = CsmsTargets.Dashboard, DiscoverRoutes = true)]
[LoginAs<CsmsOperatorLogin>("operator")]
[RequiresPlaywrightBrowser]
[RequiresDashboardBuild]
[RequiresCapability(
    ProtoCapabilityKinds.Broker,
    Reason = "The journey awaits invoice.issued on the broker; configure ProtoTest:Messaging:RabbitMq:ConnectionString.")]
[RequiresWorker<BillingWorker>]
public sealed class DashboardDepthJourney
{
    [ProtoTest]
    public async Task TheOperatorSeesTheSessionInvoiceAndTariffDepth()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();

        // A 22 kWh session billed before the browser opens: energy 8.80 + start fee 1.50 = 10.30.
        using (var started = await Proto.Context.Rest(CsmsTargets.Api)
                   .Body(new { stationId = op.StationId, connectorId = 1 })
                   .PostAsync("/api/sessions"))
        {
            started.Should.HaveHttpStatus(HttpStatusCode.Created);
            var session = started.ReadAsJson<SessionResponse>()!;
            using (var meter = await Proto.Context.Rest(CsmsTargets.Api)
                       .Body(new { totalKwh = 22m })
                       .PostAsync($"/api/sessions/{session.Id}/meter-values"))
            {
                meter.Should.HaveHttpStatus(HttpStatusCode.OK);
            }

            using (var ended = await Proto.Context.Rest(CsmsTargets.Api).PostAsync($"/api/sessions/{session.Id}/end"))
            {
                ended.Should.HaveHttpStatus(HttpStatusCode.OK);
            }

            await Proto.Context.Messaging().AwaitAsync(
                CsmsEvents.Exchange,
                candidate => CsmsMessages.IsInvoiceIssuedFor(candidate, session.Id),
                TimeSpan.FromSeconds(30));
        }

        // The station timeline carries the session with its invoice link.
        var stations = Proto.Context.Web().Page<StationsPage>();
        await stations.OpenAsync("/");
        await stations.Station(op.StationName).Link.ClickAsync();
        var station = Proto.Context.Web().Page<StationDetailPage>();
        await station.Title.Should.HaveTextAsync(op.StationName, OpenCsmsDashboard.Wait);
        var row = station.Sessions.Matching(By.HasText("22 kWh"), "Session[22 kWh]");
        await row.Connector.Should.HaveTextAsync("1", OpenCsmsDashboard.Wait);
        await row.Energy.Should.HaveTextAsync("22 kWh", OpenCsmsDashboard.Wait);
        await row.State.Should.HaveTextAsync("Ended", OpenCsmsDashboard.Wait);

        // The invoice view shows the worker's calculation lines.
        await row.Invoice.ClickAsync();
        var invoice = Proto.Context.Web().Page<InvoiceDetailPage>();
        await invoice.Title.Should.HaveTextAsync("€10.30", OpenCsmsDashboard.Wait);
        await invoice.Energy.Should.HaveTextAsync("22 kWh", OpenCsmsDashboard.Wait);
        await invoice.EnergyAmount.Should.HaveTextAsync("€8.80", OpenCsmsDashboard.Wait);
        await invoice.StartFee.Should.HaveTextAsync("€1.50", OpenCsmsDashboard.Wait);
        await invoice.IdleHours.Should.HaveTextAsync("0 started hours", OpenCsmsDashboard.Wait);
        await invoice.IdleFee.Should.HaveTextAsync("€0.00", OpenCsmsDashboard.Wait);
        await invoice.Total.Should.HaveTextAsync("€10.30", OpenCsmsDashboard.Wait);

        // The operator reprices the tariff through the dashboard form.
        var tariffs = Proto.Context.Web().Page<TariffsPage>();
        await tariffs.OpenAsync("/tariffs");
        await tariffs.Title.Should.HaveTextAsync("Tariffs", OpenCsmsDashboard.Wait);
        await tariffs.FirstEditButton.ClickAsync();
        await tariffs.EditForm.Should.BeVisibleAsync(OpenCsmsDashboard.Wait);
        await tariffs.Flow("Reprice the tariff")
            .Fill(tariff => tariff.EnergyInput, "0.55")
            .Click(tariff => tariff.Save)
            .RunAsync();
        var repriced = tariffs.Rows.Matching(By.HasText("€0.55"), "Tariff[repriced]");
        await repriced.Energy.Should.HaveTextAsync("€0.55", OpenCsmsDashboard.Wait);

        // The remote start against the unconnected charge point refuses honestly on the screen.
        await station.OpenAsync($"/stations/{op.StationId}");
        await station.Title.Should.HaveTextAsync(op.StationName, OpenCsmsDashboard.Wait);
        await station.Flow("Ask for a remote start")
            .Fill(remote => remote.RemoteIdTag, "card-1")
            .Click(remote => remote.RemoteStart)
            .RunAsync();
        await station.RemoteStartError.Should.ContainTextAsync("is not connected", OpenCsmsDashboard.Wait);
    }
}
