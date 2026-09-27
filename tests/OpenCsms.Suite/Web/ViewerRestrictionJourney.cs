namespace OpenCsms.Suite.Web;

using System.Net;
using OpenCsms.Domain;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.Web;
using ProtoTest.Web.Playwright;

/// <summary>
/// The viewer's dashboard journey: the viewer signs in on the dashboard's own sign-in screen, reads
/// what the role may read, and cannot reach the operator actions - the edit buttons and the remote
/// commands are not rendered for the role, and the API behind them already answers 403. The session
/// declares <c>DiscoverRoutes</c> so the report's page inventory comes from the live Vue Router.
/// </summary>
[Application(CsmsTargets.Api)]
[CsmsOperator]
[CsmsViewer]
[WebSession("Default", Application = CsmsTargets.Dashboard, DiscoverRoutes = true)]
[LoginAs<CsmsViewerLogin>("viewer")]
[RequiresPlaywrightBrowser]
[RequiresDashboardBuild]
public sealed class ViewerRestrictionJourney
{
    [ProtoTest]
    public async Task TheViewerReadsButCannotReachTheOperatorActions()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        var viewer = Proto.Context.Resolve<CsmsViewer>();
        Assert.That(viewer.TenantId, Is.EqualTo(op.TenantId), "the viewer reads the operator's tenant");

        // The stations list reads fine for the viewer role.
        var stations = Proto.Context.Web().Page<StationsPage>();
        await stations.OpenAsync("/");
        await stations.Title.Should.HaveTextAsync("Charging stations", OpenCsmsDashboard.Wait);
        await stations.Station(op.StationName).Link.Should.HaveTextAsync(op.StationName, OpenCsmsDashboard.Wait);

        // One open session, so the timeline renders the row an operator could stop from here.
        using (var started = await Proto.Context.Rest(CsmsTargets.Api)
                   .Body(new { stationId = op.StationId, connectorId = 1 })
                   .PostAsync("/api/sessions"))
        {
            started.Should.HaveHttpStatus(HttpStatusCode.Created);
        }

        // The station detail shows the open session but no remote command: neither the start panel
        // nor the per-session stop button is rendered for the role.
        await stations.Station(op.StationName).Link.ClickAsync();
        var station = Proto.Context.Web().Page<StationDetailPage>();
        await station.Title.Should.HaveTextAsync(op.StationName, OpenCsmsDashboard.Wait);
        var row = station.Sessions.Matching(By.HasText("Open"), "Session[open]");
        await row.State.Should.HaveTextAsync("Open", OpenCsmsDashboard.Wait);
        await row.Stop.ShouldNot.BeVisibleAsync();
        await station.RemoteStart.ShouldNot.BeVisibleAsync();

        // The tariffs read fine, with the read-only note instead of the edit buttons.
        var tariffs = Proto.Context.Web().Page<TariffsPage>();
        await tariffs.OpenAsync("/tariffs");
        await tariffs.Title.Should.HaveTextAsync("Tariffs", OpenCsmsDashboard.Wait);
        await tariffs.ReadonlyNote.Should.BeVisibleAsync(OpenCsmsDashboard.Wait);
        await tariffs.FirstEditButton.ShouldNot.BeVisibleAsync();

        // The invoices read fine too; the viewer role hides actions, not data.
        var invoices = Proto.Context.Web().Page<InvoicesPage>();
        await invoices.OpenAsync("/invoices");
        await invoices.Title.Should.HaveTextAsync("Invoices", OpenCsmsDashboard.Wait);
        await invoices.Empty.Should.BeVisibleAsync(OpenCsmsDashboard.Wait);
    }
}
