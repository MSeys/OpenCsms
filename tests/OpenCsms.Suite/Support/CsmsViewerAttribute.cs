namespace OpenCsms.Suite.Support;

using OpenCsms.Domain;
using ProtoTest.Core;

/// <summary>
/// Provisions a viewer for the test's operator tenant, after the operator provisioning (order -50,
/// between the operator at -100 and the sign-in at 0). The viewer rides the operator's machine
/// credential, which the provisioning carries for the credential's tenant. Viewer tests sign in
/// through <see cref="Web.CsmsViewerLogin"/> with the <see cref="CsmsViewer"/> this attribute stores;
/// the account itself is created through the product's front door, exactly like the operator's.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class CsmsViewerProvisioningAttribute : ProtoAttribute
{
    public CsmsViewerProvisioningAttribute()
    {
        Order = -50;
    }

    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var machine = context.Resolve<CsmsMachine>();
        var email = $"{context.UniqueName("viewer")}@opencsms.test";
        var password = context.UniqueName("viewer-secret");

        await CsmsProvisioning.ProvisionUserAsync(
            context,
            email,
            context.UniqueName("Viewer"),
            password,
            UserRoles.Viewer);

        context.SetContext(new CsmsViewer(machine.TenantId, email, password));
    }
}

/// <summary>
/// The viewer account a test acts as: a viewer for the operator tenant the test declared. One
/// declaration reads as the test's configuration; the provisioning itself stays a plain attribute
/// (<see cref="CsmsViewerProvisioningAttribute"/>), which still needs the operator's machine
/// credential, so a viewer test declares <see cref="CsmsOperatorAttribute"/> beside it.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class CsmsViewerAttribute : ProtoCompositeAttribute
{
    protected override IReadOnlyList<Attribute> Compose() => [new CsmsViewerProvisioningAttribute()];
}

/// <summary>
/// The viewer account one test provisioned: the tenant it reads and its login. <c>Password</c> is one
/// of the framework's default sensitive names, so the context trace shows it redacted.
/// </summary>
public sealed record CsmsViewer(string TenantId, string LoginEmail, string Password) : IProtoContext;
