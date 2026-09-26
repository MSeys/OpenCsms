namespace OpenCsms.Suite.Support;

using System.Net;
using OpenCsms.Api;
using OpenCsms.Domain;
using ProtoTest.Core;
using ProtoTest.Rest;

/// <summary>
/// Provisions this test's isolated operator - a tenant, its tariff, its station and the operator
/// admin's dashboard account - with names from <see cref="ProtoExecutionContext.UniqueName(string, int)"/>,
/// so a journey can run repeatedly against a database that outlives the test process. The test reads
/// the provisioned <see cref="CsmsOperator"/>, including the login the browser journeys sign in with.
/// Provisioning runs before every other setup attribute (<c>Order</c> -100), because a login needs the
/// account this attribute creates.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class CsmsOperatorAttribute : ProtoAttribute
{
    public CsmsOperatorAttribute()
    {
        // The account must exist before [LoginAs] (order 0) signs in with it.
        Order = -100;
    }

    /// <summary>Connectors on the provisioned station; a test can ask for a smaller or larger one.</summary>
    public int ConnectorCount { get; init; } = 2;

    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var tenantId = context.UniqueName("op");
        var chargePointId = context.UniqueName("cp");
        var stationName = context.UniqueName("station");

        // Provisioning is the machine API's job, not the browser application's: a test whose
        // [Application] is the dashboard still arranges through the in-process server.
        using var tariffResponse = await context.Rest(CsmsTargets.Api)
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

        using var stationResponse = await context.Rest(CsmsTargets.Api)
            .Body(new
            {
                tenantId,
                chargePointId,
                name = stationName,
                connectorCount = ConnectorCount,
                tariffId = tariff.Id
            })
            .PostAsync("/api/stations");
        stationResponse.Should.HaveHttpStatus(HttpStatusCode.Created);
        var station = stationResponse.ReadAsJson<StationResponse>()
            ?? throw new InvalidOperationException("Registering the station answered an empty body.");

        var loginEmail = $"{context.UniqueName("operator")}@opencsms.test";
        var loginPassword = context.UniqueName("secret");
        using var userResponse = await context.Rest(CsmsTargets.Api)
            .Body(new
            {
                tenantId,
                email = loginEmail,
                displayName = context.UniqueName("Operator"),
                password = loginPassword,
                role = UserRoles.Operator
            })
            .PostAsync("/api/users");
        userResponse.Should.HaveHttpStatus(HttpStatusCode.Created);

        context.SetContext(new CsmsOperator(
            tenantId,
            chargePointId,
            stationName,
            tariff.Id,
            station.Id,
            ConnectorCount,
            loginEmail,
            loginPassword));
    }
}

/// <summary>
/// The operator one test provisioned: its tenant, tariff, station and the operator admin's login.
/// </summary>
public sealed record CsmsOperator(
    string TenantId,
    string ChargePointId,
    string StationName,
    Guid TariffId,
    Guid StationId,
    int ConnectorCount,
    string LoginEmail,
    string LoginPassword) : IProtoContext;
