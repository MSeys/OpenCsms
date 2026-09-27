namespace OpenCsms.Suite.Web;

using System.Globalization;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Web;
using ProtoTest.Web.Playwright;

/// <summary>
/// The operator's dashboard journey, driven in a real browser against the API hosted on the run's
/// loopback listener: the login strategy signs in on the dashboard's own sign-in screen with the
/// account the provisioning attribute created, the stations list shows the one station this test's
/// tenant owns, and its detail screen shows the empty session list. The session is declared with
/// <c>DiscoverRoutes</c> so the report's page inventory comes from the live Vue Router.
/// </summary>
[Application(CsmsTargets.Api)]
[CsmsOperator]
[WebSession("Default", Application = CsmsTargets.Dashboard, DiscoverRoutes = true)]
[LoginAs<CsmsOperatorLogin>("operator")]
[RequiresPlaywrightBrowser]
[RequiresDashboardBuild]
public sealed class OperatorDashboardJourney
{
    [ProtoTest]
    public async Task TheOperatorSignsInAndSeesTheirStation()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();

        // The login strategy signed in through /sign-in; the landing screen is the stations list.
        var stations = Proto.Context.Web().Page<StationsPage>();
        await stations.OpenAsync("/");
        await stations.Title.Should.HaveTextAsync("Charging stations", OpenCsmsDashboard.Wait);

        // The operator's own station is listed with the facts the API stored for it.
        var row = stations.Station(op.StationName);
        await row.Link.Should.HaveTextAsync(op.StationName, OpenCsmsDashboard.Wait);
        await row.ChargePoint.Should.HaveTextAsync(op.ChargePointId, OpenCsmsDashboard.Wait);
        await row.Connectors.Should.HaveTextAsync(
            op.ConnectorCount.ToString(CultureInfo.InvariantCulture),
            OpenCsmsDashboard.Wait);

        // The detail screen follows the link and shows the station's (empty) session list.
        await row.Link.ClickAsync();
        var station = Proto.Context.Web().Page<StationDetailPage>();
        await station.Title.Should.HaveTextAsync(op.StationName, OpenCsmsDashboard.Wait);
        await station.ChargePoint.Should.HaveTextAsync(op.ChargePointId, OpenCsmsDashboard.Wait);
        await station.SessionsEmpty.Should.BeVisibleAsync(OpenCsmsDashboard.Wait);
    }
}
