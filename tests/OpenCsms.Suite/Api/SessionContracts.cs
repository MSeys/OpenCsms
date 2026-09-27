namespace OpenCsms.Suite.Api;

using System.Net;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.NUnit;
using ProtoTest.Rest;

/// <summary>
/// The synchronous REST contracts around a charging session: bad input is rejected before any billing
/// happens. Both tests reach the machine surface, so both bring an operator's tenant credential:
/// the connector test's station rule and the unknown-session test's 404 are answered for the
/// credential's tenant.
/// </summary>
[Application(CsmsTargets.Api)]
[Auth<CsmsMachineKeyAuthenticator>]
public sealed class SessionContracts
{
    [ProtoTest]
    [CsmsOperator]
    public async Task AConnectorOutsideTheStationIsRejected()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();

        using var response = await Proto.Context.Rest()
            .Body(new { stationId = op.StationId, connectorId = op.ConnectorCount + 1 })
            .PostAsync("/api/sessions");

        response.Should.HaveHttpStatus(HttpStatusCode.BadRequest);
    }

    [ProtoTest]
    [CsmsOperator]
    public async Task AnUnknownSessionHasNoInvoice()
    {
        using var response = await Proto.Context.Rest().GetAsync($"/api/sessions/{Guid.NewGuid()}/invoice");

        response.Should.HaveHttpStatus(HttpStatusCode.NotFound);
    }
}
