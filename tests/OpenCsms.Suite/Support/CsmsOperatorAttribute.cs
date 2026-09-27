namespace OpenCsms.Suite.Support;

using ProtoTest.Core;

/// <summary>
/// Provisions this test's isolated operator - a tenant with its machine credential, its tariff, its
/// station and the operator admin's dashboard account - with names from
/// <see cref="ProtoExecutionContext.UniqueName(string, int)"/>, so a journey can run repeatedly
/// against a database that outlives the test process. The test reads the provisioned
/// <see cref="CsmsOperator"/>, including the login the browser journeys sign in with and the key the
/// management calls carry. The orchestration is <see cref="CsmsProvisioning"/> over the Data
/// provisioners; the route stays the product's front door. Provisioning runs before every other setup
/// attribute (<c>Order</c> -100), because a login needs the account this attribute creates.
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
        var provisioned = await CsmsProvisioning.ProvisionOperatorAsync(
            context,
            context.UniqueName("tenant"),
            context.UniqueName("cp"),
            context.UniqueName("station"),
            context.UniqueName("tariff"),
            ConnectorCount,
            $"{context.UniqueName("operator")}@opencsms.test",
            context.UniqueName("secret"),
            context.UniqueName("Operator"));
        context.SetContext(provisioned);
    }
}

/// <summary>
/// The operator one test provisioned: its tenant, tariff, station, the operator admin's login and the
/// tenant's machine key. <c>Password</c> and <c>ApiKey</c> are the framework's default sensitive
/// names, so the context trace shows them redacted.
/// </summary>
public sealed record CsmsOperator(
    string TenantId,
    string ChargePointId,
    string StationName,
    Guid TariffId,
    Guid StationId,
    int ConnectorCount,
    string LoginEmail,
    string Password,
    string ApiKey) : IProtoContext;
