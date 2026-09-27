namespace OpenCsms.Suite.Api;

using System.Net;
using OpenCsms.Contracts;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Http.Authenticators;
using ProtoTest.NUnit;
using ProtoTest.Rest;

/// <summary>
/// The machine surface's credential contracts: the management routes answer 401 without a key and
/// with a key no tenant owns, a signed-in dashboard cookie is not a machine credential, and a valid
/// key reaches exactly the tenant it was issued for - a second tenant's rows answer 404 like unknown
/// ones, and nothing a refused call sent is stored. The tenant and its key come from
/// <see cref="CsmsOperatorAttribute"/> through the product's own registration route.
/// </summary>
[Application(CsmsTargets.Api)]
[CsmsOperator]
[Auth<CsmsMachineKeyAuthenticator>]
public sealed class MachineApiCredentials
{
    [ProtoTest]
    public async Task TheMachineSurfaceAnswersUnauthorizedWithoutAKeyOrWithAWrongOne()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        var missingName = Proto.Context.UniqueName("refused-missing-key");
        var wrongName = Proto.Context.UniqueName("refused-wrong-key");

        // No key: the request carries no credential of its own.
        using (var missing = await Proto.Context.Rest()
                   .WithoutAuth()
                   .Body(new { name = missingName, energyPricePerKwh = 0.40m })
                   .PostAsync("/api/tariffs"))
        {
            missing.Should.HaveHttpStatus(HttpStatusCode.Unauthorized);
        }

        // A key no tenant owns: the same refusal.
        using (var wrong = await Proto.Context.Rest()
                   .WithoutAuth()
                   .Header(CsmsMachine.HeaderName, "ocsms_not-a-tenant-key")
                   .Body(new { name = wrongName, energyPricePerKwh = 0.40m })
                   .PostAsync("/api/tariffs"))
        {
            wrong.Should.HaveHttpStatus(HttpStatusCode.Unauthorized);
        }

        // The dashboard's cookie is a session credential, not a machine one.
        await DashboardSession.SignInAsync(op.LoginEmail, op.Password);
        using (var cookieOnly = await Proto.Context.Rest().WithoutAuth().GetAsync("/api/tariffs"))
        {
            cookieOnly.Should.HaveHttpStatus(HttpStatusCode.Unauthorized);
        }

        // Neither refused call stored anything, and the same route with the valid key shows it.
        using (var listed = await Proto.Context.Rest().GetAsync("/api/tariffs"))
        {
            listed.Should.HaveHttpStatus(HttpStatusCode.OK);
            var names = listed.ReadAsJson<TariffResponse[]>()!.Select(tariff => tariff.Name);
            Assert.That(names, Does.Not.Contain(missingName).And.Not.Contain(wrongName));
        }

        using (var created = await Proto.Context.Rest()
                   .Body(new { name = missingName, energyPricePerKwh = 0.40m })
                   .PostAsync("/api/tariffs"))
        {
            created.Should.HaveHttpStatus(HttpStatusCode.Created);
        }
    }

    [ProtoTest]
    public async Task AKeyReachesOnlyItsOwnTenantsRows()
    {
        var own = Proto.Context.Resolve<CsmsOperator>();
        var other = await TenantProvisioning.ProvisionAsync(Proto.Context, "machine-neighbor");

        // The test's key cannot see the neighbor's station, exactly like an unknown one.
        using (var foreign = await Proto.Context.Rest().GetAsync($"/api/stations/{other.StationId}"))
        {
            foreign.Should.HaveHttpStatus(HttpStatusCode.NotFound);
        }

        using (var foreignConnectors = await Proto.Context.Rest()
                   .GetAsync($"/api/stations/{other.StationId}/connectors"))
        {
            foreignConnectors.Should.HaveHttpStatus(HttpStatusCode.NotFound);
        }

        // ... nor act on it: the session start is refused before the station is touched.
        using (var start = await Proto.Context.Rest()
                   .Body(new { stationId = other.StationId, connectorId = 1 })
                   .PostAsync("/api/sessions"))
        {
            start.Should.HaveHttpStatus(HttpStatusCode.NotFound);
        }

        // The neighbor's own key reaches the same station, so the refusals were the scoping.
        using (var reachable = await Proto.Context.Rest()
                   .Auth(new ApiKeyAuthenticator(CsmsMachine.HeaderName, other.ApiKey))
                   .GetAsync($"/api/stations/{other.StationId}"))
        {
            reachable.Should.HaveHttpStatus(HttpStatusCode.OK);
        }

        // A session of the test's own tenant: the neighbor can neither read nor end it.
        Guid sessionId;
        using (var created = await Proto.Context.Rest()
                   .Body(new { stationId = own.StationId, connectorId = 1 })
                   .PostAsync("/api/sessions"))
        {
            created.Should.HaveHttpStatus(HttpStatusCode.Created);
            sessionId = created.ReadAsJson<SessionResponse>()!.Id;
        }

        using (var foreignRead = await Proto.Context.Rest()
                   .Auth(new ApiKeyAuthenticator(CsmsMachine.HeaderName, other.ApiKey))
                   .GetAsync($"/api/sessions/{sessionId}"))
        {
            foreignRead.Should.HaveHttpStatus(HttpStatusCode.NotFound);
        }

        using (var foreignEnd = await Proto.Context.Rest()
                   .Auth(new ApiKeyAuthenticator(CsmsMachine.HeaderName, other.ApiKey))
                   .PostAsync($"/api/sessions/{sessionId}/end"))
        {
            foreignEnd.Should.HaveHttpStatus(HttpStatusCode.NotFound);
        }

        using (var stillOpen = await Proto.Context.Rest().GetAsync($"/api/sessions/{sessionId}"))
        {
            stillOpen.Should.HaveHttpStatus(HttpStatusCode.OK);
            Assert.That(
                stillOpen.ReadAsJson<SessionResponse>()!.IsOpen,
                Is.True,
                "the refused foreign end changed nothing");
        }
    }
}
