namespace OpenCsms.Suite.Web;

using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Web;
using ProtoTest.Web.Playwright;

/// <summary>
/// The public status page journey: no sign-in, no cookie, the same shell. The test's tenant is
/// provisioned like any other journey's, but the browser is an anonymous visitor who finds the
/// station and its connector state on the public screen, with the shell offering sign-in instead of
/// a session.
/// </summary>
[Application(CsmsTargets.Api)]
[CsmsOperator]
[WebSession("Default", Application = CsmsTargets.Dashboard, DiscoverRoutes = true, Open = "/status")]
[RequiresPlaywrightBrowser]
[RequiresDashboardBuild]
public sealed class PublicStatusJourney
{
    [ProtoTest]
    public async Task ThePublicStatusPageShowsAStationWithoutSigningIn()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();

        var status = Proto.Context.Web().Page<StatusPage>();
        await status.OpenAsync("/status");
        await status.Title.Should.HaveTextAsync("Network status", OpenCsmsDashboard.Wait);

        // The provisioned station is on the public page with no session required.
        var card = status.Station(op.StationName);
        await card.Name.Should.HaveTextAsync(op.StationName, OpenCsmsDashboard.Wait);
        await card.ChargePoint.Should.HaveTextAsync(op.ChargePointId, OpenCsmsDashboard.Wait);

        // A fresh station has no connector statuses yet, and the card says so honestly.
        await card.NoConnectors.Should.BeVisibleAsync(OpenCsmsDashboard.Wait);

        // Anonymous means the shell offers sign-in and has no session facts to show.
        await status.SignInLink.Should.BeVisibleAsync(OpenCsmsDashboard.Wait);
        await status.SessionUser.ShouldNot.BeVisibleAsync(OpenCsmsDashboard.Wait);
    }
}
