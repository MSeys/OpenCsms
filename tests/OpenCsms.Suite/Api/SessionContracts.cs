namespace OpenCsms.Suite.Api;

using System.Net;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

/// <summary>
/// The synchronous REST contracts around a charging session: bad input is rejected before any billing
/// happens. The connector test provisions its operator through <see cref="CsmsOperatorAttribute"/>;
/// the unknown-session test touches no provisioned state, so it needs no REST writes of its own.
/// </summary>
[Application(CsmsTargets.Api)]
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
    public async Task AnUnknownSessionHasNoInvoice()
    {
        using var response = await Proto.Context.Rest().GetAsync($"/api/sessions/{Guid.NewGuid()}/invoice");

        response.Should.HaveHttpStatus(HttpStatusCode.NotFound);
    }
}
