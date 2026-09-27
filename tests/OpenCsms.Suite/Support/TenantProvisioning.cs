namespace OpenCsms.Suite.Support;

using ProtoTest.Core;

/// <summary>
/// Provisions a second operator tenant through the product's front door - tariff, station and
/// operator admin - for the tests that prove one tenant cannot read another's rows. Names come
/// from <see cref="ProtoExecutionContext.UniqueName(string, int)"/>, so the tenant survives reruns
/// against a database that outlives the test process. The orchestration is the shared
/// <see cref="CsmsProvisioning"/> over the Data provisioners, the same route
/// <see cref="CsmsOperatorAttribute"/> takes.
/// </summary>
public static class TenantProvisioning
{
    /// <summary>Provisions the tenant and returns its operator, station and login.</summary>
    public static Task<CsmsOperator> ProvisionAsync(
        ProtoExecutionContext context,
        string prefix,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CsmsProvisioning.ProvisionOperatorAsync(
            context,
            context.UniqueName($"{prefix}-tenant"),
            context.UniqueName($"{prefix}-cp"),
            context.UniqueName($"{prefix}-station"),
            context.UniqueName($"{prefix}-tariff"),
            2,
            $"{context.UniqueName($"{prefix}-operator")}@opencsms.test",
            context.UniqueName($"{prefix}-secret"),
            context.UniqueName($"{prefix}-Operator"),
            cancellationToken);
    }
}
