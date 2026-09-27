namespace OpenCsms.Api;

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using OpenCsms.Application.Identity;
using OpenCsms.Domain;

/// <summary>
/// The machine surface's credential: the <c>X-Api-Key</c> header carries a tenant's key, the
/// application resolves the tenant the hash belongs to, and the principal claims that tenant - the
/// routes never read a tenant from the request. A missing or unknown key is answered 401 through the
/// scheme's challenge; the header value is never traced or logged.
/// </summary>
internal static class CsmsApiKeyAuthentication
{
    public const string Scheme = "CsmsApiKey";
    public const string HeaderName = "X-Api-Key";
}

/// <summary>Authenticates a machine request from the API key header.</summary>
internal sealed class CsmsApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TenantAuthentication authentication)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = Request.Headers[CsmsApiKeyAuthentication.HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(presented))
        {
            return AuthenticateResult.NoResult();
        }

        var tenant = await authentication.AuthenticateAsync(presented, Context.RequestAborted);
        if (tenant is null)
        {
            return AuthenticateResult.Fail("The machine API key is not valid.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, tenant.Id),
                new Claim(ClaimTypes.Name, tenant.Name),
                new Claim(UserClaimTypes.TenantId, tenant.Id)
            ],
            CsmsApiKeyAuthentication.Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
