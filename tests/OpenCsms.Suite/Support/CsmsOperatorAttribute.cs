namespace OpenCsms.Suite.Support;

using System.Net;
using OpenCsms.Api;
using ProtoTest.Core;
using ProtoTest.Rest;

/// <summary>
/// Provisions this test's isolated operator - a tenant, its tariff and its station - with names derived
/// from <see cref="ProtoExecutionContext.TestId"/>, so a journey can run repeatedly against a database
/// that outlives the test process. The test reads the provisioned <see cref="CsmsOperator"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class CsmsOperatorAttribute : ProtoAttribute
{
    /// <summary>Connectors on the provisioned station; a test can ask for a smaller or larger one.</summary>
    public int ConnectorCount { get; init; } = 2;

    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var tenantId = $"op-{context.TestId}";

        using var tariffResponse = await context.Rest()
            .Body(new
            {
                tenantId,
                name = $"tariff-{context.TestId}",
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
                name = $"station-{context.TestId}",
                connectorCount = ConnectorCount,
                tariffId = tariff.Id
            })
            .PostAsync("/api/stations");
        stationResponse.Should.HaveHttpStatus(HttpStatusCode.Created);
        var station = stationResponse.ReadAsJson<StationResponse>()
            ?? throw new InvalidOperationException("Registering the station answered an empty body.");

        context.SetContext(new CsmsOperator(tenantId, tariff.Id, station.Id, ConnectorCount));
    }
}

/// <summary>The operator one test provisioned: its tenant and the tariff and station registered for it.</summary>
public sealed record CsmsOperator(string TenantId, Guid TariffId, Guid StationId, int ConnectorCount) : IProtoContext;
