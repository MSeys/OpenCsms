namespace OpenCsms.Suite.Web;

using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Web;
using ProtoTest.Web.Playwright;

/// <summary>
/// The tenant isolation browser journey: signed in as the test's operator, the stations list shows
/// only this tenant's station, and opening another tenant's station answers the dashboard's
/// not-found panel instead of its rows. The API-level isolation lives in
/// <see cref="Api.TenantIsolation"/>; this journey proves the dashboard is honest about the same rule.
/// </summary>
[Application(CsmsTargets.Dashboard)]
[CsmsOperator]
[WebSession("Default", Application = CsmsTargets.Dashboard, DiscoverRoutes = true)]
[LoginAs<CsmsOperatorLogin>("operator")]
[RequiresPlaywrightBrowser]
[RequiresDashboardBuild]
public sealed class TenantIsolationJourney
{
    [ProtoTest]
    public async Task TheOperatorCannotOpenAnotherTenantsStation()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        var other = await TenantProvisioning.ProvisionAsync(Proto.Context, "neighbor");

        var stations = Proto.Context.Web().Page<StationsPage>();
        await stations.OpenAsync("/");
        await stations.Title.Should.HaveTextAsync("Charging stations", OpenCsmsDashboard.Wait);
        await stations.Station(op.StationName).Link.Should.HaveTextAsync(op.StationName, OpenCsmsDashboard.Wait);
        await stations.Station(other.StationName).Link.ShouldNot.BeVisibleAsync();

        var station = Proto.Context.Web().Page<StationDetailPage>();
        await station.OpenAsync($"/stations/{other.StationId}");
        await station.Page.Should.BeVisibleAsync(OpenCsmsDashboard.Wait);
        await station.StationError.Should.BeVisibleAsync(OpenCsmsDashboard.Wait);
        await station.Title.ShouldNot.HaveTextAsync(other.StationName);
    }
}
