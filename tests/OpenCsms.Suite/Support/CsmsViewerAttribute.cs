namespace OpenCsms.Suite.Support;

using OpenCsms.Domain;
using ProtoTest.Core;

/// <summary>
/// Provisions a viewer for the test's operator tenant, after <see cref="CsmsOperatorAttribute"/>
/// (order -50, between the operator at -100 and the sign-in at 0). Viewer tests sign in through
/// <see cref="Web.CsmsViewerLogin"/> with the <see cref="CsmsViewer"/> this attribute stores; the
/// account itself is created through the product's front door, exactly like the operator's.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class CsmsViewerAttribute : ProtoAttribute
{
    public CsmsViewerAttribute()
    {
        Order = -50;
    }

    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var op = context.Resolve<CsmsOperator>();
        var email = $"{context.UniqueName("viewer")}@opencsms.test";
        var password = context.UniqueName("viewer-secret");

        await CsmsProvisioning.ProvisionUserAsync(
            context,
            op.TenantId,
            email,
            context.UniqueName("Viewer"),
            password,
            UserRoles.Viewer);

        context.SetContext(new CsmsViewer(op.TenantId, email, password));
    }
}

/// <summary>The viewer account one test provisioned: the tenant it reads and its login.</summary>
public sealed record CsmsViewer(string TenantId, string LoginEmail, string LoginPassword) : IProtoContext;
