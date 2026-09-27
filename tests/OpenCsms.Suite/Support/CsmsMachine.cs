namespace OpenCsms.Suite.Support;

using ProtoTest.Core;

/// <summary>
/// The machine credential one tenant's rows are provisioned under and every management call carries:
/// the tenant the key was issued for and the raw key returned once at registration. The property is
/// named <c>ApiKey</c> because that is one of the framework's default sensitive names, so the trace
/// shows <c>[REDACTED]</c> wherever the record appears.
/// </summary>
public sealed record CsmsMachine(string TenantId, string ApiKey) : IProtoContext
{
    /// <summary>The header the machine surface reads; ProtoTest's diagnostics redact it by default.</summary>
    public const string HeaderName = "X-Api-Key";
}
