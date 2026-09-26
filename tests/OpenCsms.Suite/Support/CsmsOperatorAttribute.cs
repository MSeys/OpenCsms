namespace OpenCsms.Suite.Support;

using System.Net;
using OpenCsms.Api;
using ProtoTest.Core;
using ProtoTest.Rest;

/// <summary>
/// Provisions this test's isolated operator - a tenant, its tariff and its station - with names from
/// <see cref="ProtoExecutionContext.UniqueName(string, int)"/>, so a journey can run repeatedly against
/// a database that outlives the test process. The test reads the provisioned <see cref="CsmsOperator"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class CsmsOperatorAttribute : ProtoAttribute
{
    /// <summary>Connectors on the provisioned station; a test can ask for a smaller or larger one.</summary>
    public int ConnectorCount { get; init; } = 2;

    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var tenantId = context.UniqueName("op");
        var chargePointId = context.UniqueName("cp");

        using var tariffResponse = await context.Rest()
            .Body(new
            {
                tenantId,
                name = context.UniqueName("tariff"),
                energyPricePerKwh = 0.40m,
                startFee = 1.50m,
                idleFeePerHour = 2.00m,
                idleGracePeriod = TimeSpan.FromMinutes(10)
            })
            .PostAsync("/api/tariffs");
        tariffResponse.Should.HaveHttpStatus(HttpStatusCode.Created);
        var tariff = tariffResponse.ReadAsJson<TariffResponse>()
            ?? throw new InvalidOperationException("Registering the tariff answered an empty body.");

        using var stationResponse = await context.Rest()
            .Body(new
            {
                tenantId,
                chargePointId,
                name = context.UniqueName("station"),
                connectorCount = ConnectorCount,
                tariffId = tariff.Id
            })
            .PostAsync("/api/stations");
        stationResponse.Should.HaveHttpStatus(HttpStatusCode.Created);
        var station = stationResponse.ReadAsJson<StationResponse>()
            ?? throw new InvalidOperationException("Registering the station answered an empty body.");

        context.SetContext(new CsmsOperator(tenantId, chargePointId, tariff.Id, station.Id, ConnectorCount));
    }
}

/// <summary>The operator one test provisioned: its tenant and the tariff and station registered for it.</summary>
public sealed record CsmsOperator(
    string TenantId,
    string ChargePointId,
    Guid TariffId,
    Guid StationId,
    int ConnectorCount) : IProtoContext;
