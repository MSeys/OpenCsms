namespace OpenCsms.Api;

using System.Security.Claims;
using OpenCsms.Domain;

/// <summary>The claims the machine and dashboard principals share, read in one place.</summary>
internal static class CsmsTenantClaims
{
    /// <summary>The tenant the authenticated principal is scoped to.</summary>
    public static string TenantId(ClaimsPrincipal user)
        => user.FindFirstValue(UserClaimTypes.TenantId)
           ?? throw new InvalidOperationException("The authenticated principal carries no tenant claim.");
}
