namespace OpenCsms.Suite.Support;

using ProtoTest.Core;

/// <summary>
/// Provisions this test's isolated operator, with names from
/// <see cref="ProtoExecutionContext.UniqueName(string, int)"/>, so a journey can run repeatedly against
/// a database that outlives the test process.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class CsmsOperatorProvisioningAttribute : ProtoAttribute
{
    public CsmsOperatorProvisioningAttribute(int connectorCount)
    {
        ConnectorCount = connectorCount;
        // The account must exist before [LoginAs] (order 0) signs in with it.
        Order = -100;
    }

    /// <summary>Connectors on the provisioned station; a test can ask for a smaller or larger one.</summary>
    public int ConnectorCount { get; }

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
/// The operator a test acts as, as one declaration; the provisioning itself is a plain attribute with
/// the parameters passed through this composite's constructor.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class CsmsOperatorAttribute(int connectorCount = 2) : ProtoCompositeAttribute
{
    /// <summary>Connectors on the provisioned station; a test can ask for a smaller or larger one.</summary>
    public int ConnectorCount { get; } = connectorCount;

    protected override IReadOnlyList<Attribute> Compose() =>
        [new CsmsOperatorProvisioningAttribute(ConnectorCount)];
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
