namespace OpenCsms.Suite.Web;

using System.Net;
using OpenCsms.Contracts;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Messaging;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.Sheets;
using ProtoTest.Web;
using ProtoTest.Web.Playwright;
using BillingWorker = OpenCsms.Billing.Worker.Program;

/// <summary>
/// The invoices screen's monthly download, clicked in a real browser: the file the download button
/// saves is the tenant's own billed month, asserted with <see cref="ProtoTest.Sheets"/> on the real
/// cells - the header, the billed row and the summary - not just the endpoint the REST journey
/// covers. The download is captured with the framework's download support and registered as a test
/// attachment, so the trace shows the file the browser saved.
/// </summary>
[Application(CsmsTargets.Api)]
[CsmsOperator]
[Auth<CsmsMachineKeyAuthenticator>]
[WebSession("Default", Application = CsmsTargets.Dashboard, DiscoverRoutes = true)]
[LoginAs<CsmsOperatorLogin>("operator")]
[RequiresPlaywrightBrowser]
[RequiresDashboardBuild]
[RequiresCapability(
    ProtoCapabilityKinds.Broker,
    Reason = "The journey awaits invoice.issued on the broker; configure ProtoTest:Messaging:RabbitMq:ConnectionString.")]
[RequiresWorker<BillingWorker>]
public sealed class InvoiceExportJourney
{
    [ProtoTest]
    [RequiresTestClock]
    public async Task TheOperatorDownloadsTheMonthlyExportFromTheInvoicesScreen()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();

        // One billed session in the run clock's month, arranged through the API: energy 8.80 plus
        // the 1.50 start fee is the 10.30 the downloaded cells must carry.
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

        // Act: the invoices screen's own download button saves the month the session was billed in.
        var invoices = Proto.Context.Web().Page<InvoicesPage>();
        await invoices.OpenAsync("/invoices");
        await invoices.Title.Should.HaveTextAsync("Invoices", OpenCsmsDashboard.Wait);
        await invoices.ExportMonth.FillAsync("2030-06");
        // The file name the download suggests is the product's own content-disposition, so the
        // capture asks for no name: passing one would replace what the assertion pins.
        var download = await invoices.DownloadAsync(
            cancellationToken => invoices.ExportDownload.ClickAsync(cancellationToken).AsTask());

        Assert.That(download.FileName, Is.EqualTo("invoices-2030-06.xlsx"), "the download carries the month's file name");

        // Assert: the real cells, not the endpoint. The model declares the header and the summary
        // labels, the one billed row carries its energy and total, and the summary scopes the same month.
        var workbook = Proto.Context.Sheets().Open(download);
        var model = workbook.Model<InvoiceExportRow>();
        model.Should.MatchHeaders();
        model.Should.MatchModel();
        Assert.That(model.Rows, Has.Count.EqualTo(1), "one row under the header for the one billed session");
        var row = model.Rows.Single();
        Assert.That(row.EnergyKwh, Is.EqualTo(22m), "the meter value the journey recorded");
        Assert.That(row.Total, Is.EqualTo(10.3m), "energy 8.80 plus the 1.50 start fee");

        var summary = workbook.KeyValueModel<InvoiceExportSummary>();
        summary.Should.MatchModel();
        summary.Column(entry => entry.Month).Should.Be("2030-06");
        summary.Column(entry => entry.Count).Should.Be(1);
        summary.Column(entry => entry.Total).Should.Be(10.3m);
        summary.Column(entry => entry.Currency).Should.Be("EUR");
    }
}
