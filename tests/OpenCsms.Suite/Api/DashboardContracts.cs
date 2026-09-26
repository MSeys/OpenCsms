namespace OpenCsms.Suite.Api;

using System.Net;
using OpenCsms.Contracts;
using OpenCsms.Domain;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

/// <summary>
/// The dashboard's API contracts, without a browser: the read surface is closed to anonymous callers
/// and open to any signed-in user, the public status surface is open to everyone, and the operator
/// commands distinguish the two roles - a viewer is refused (403) before anything reaches a charge
/// point, and the operator reaches the device path (409, because the provisioned charge point is not
/// connected). The cookie is what carries the role; the suite signs in through the product's own
/// endpoint, exactly as the SPA does.
/// </summary>
[Application(CsmsTargets.Api)]
public sealed class DashboardContracts
{
    [ProtoTest]
    public async Task TheDashboardRefusesAnonymousReads()
    {
        using var response = await Proto.Context.Rest().GetAsync("/api/dashboard/stations");

        response.Should.HaveHttpStatus(HttpStatusCode.Unauthorized);
    }

    [ProtoTest]
    public async Task ThePublicStatusNeedsNoAccount()
    {
        using var response = await Proto.Context.Rest().GetAsync("/api/status/stations");

        response.Should.HaveHttpStatus(HttpStatusCode.OK);
    }

    [ProtoTest]
    [CsmsOperator]
    public async Task TheOperatorReadsTheirStationThroughTheDashboard()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        await SignInAsync(op.LoginEmail, op.LoginPassword);

        using (var stations = await Proto.Context.Rest().GetAsync("/api/dashboard/stations"))
        {
            stations.Should.HaveHttpStatus(HttpStatusCode.OK);
            var listed = stations.ReadAsJson<DashboardStationResponse[]>()!;
            Assert.That(
                listed.Select(station => station.Id),
                Does.Contain(op.StationId),
                "the signed-in operator's own station is listed");
        }

        using var sessions = await Proto.Context.Rest().GetAsync($"/api/dashboard/stations/{op.StationId}/sessions");
        sessions.Should.HaveHttpStatus(HttpStatusCode.OK);
        Assert.That(sessions.ReadAsJson<SessionResponse[]>(), Is.Empty);
    }

    [ProtoTest]
    [CsmsOperator]
    public async Task AViewerReadsButCannotInvokeTheOperatorCommands()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        var viewerEmail = $"{Proto.Context.UniqueName("viewer")}@opencsms.test";
        var viewerPassword = Proto.Context.UniqueName("secret");
        await CsmsProvisioning.ProvisionUserAsync(
            Proto.Context, op.TenantId, viewerEmail, "Viewer", viewerPassword, UserRoles.Viewer);

        await SignInAsync(viewerEmail, viewerPassword);

        using (var reads = await Proto.Context.Rest().GetAsync("/api/dashboard/stations"))
        {
            reads.Should.HaveHttpStatus(HttpStatusCode.OK);
        }

        var token = await AntiforgeryTokenAsync();
        using var command = await Proto.Context.Rest()
            .Header("X-XSRF-TOKEN", token)
            .Body(new { idTag = "card-1" })
            .PostAsync($"/api/dashboard/stations/{op.StationId}/remote-start");
        command.Should.HaveHttpStatus(HttpStatusCode.Forbidden);
    }

    [ProtoTest]
    [CsmsOperator]
    public async Task AnOperatorReachesTheCommandThroughTheDashboard()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        await SignInAsync(op.LoginEmail, op.LoginPassword);

        var token = await AntiforgeryTokenAsync();
        using var response = await Proto.Context.Rest()
            .Header("X-XSRF-TOKEN", token)
            .Body(new { idTag = "card-1" })
            .PostAsync($"/api/dashboard/stations/{op.StationId}/remote-start");

        // The station belongs to the signed-in operator, so the role check passed and the product's
        // own device path answered: the charge point is not connected, which is a 409.
        response.Should.HaveHttpStatus(HttpStatusCode.Conflict);
    }

    private static Task SignInAsync(string email, string password)
        => DashboardSession.SignInAsync(email, password);

    private static Task<string> AntiforgeryTokenAsync()
        => DashboardSession.AntiforgeryTokenAsync();
}
