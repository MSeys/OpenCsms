namespace OpenCsms.Api;

using OpenCsms.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

/// <summary>
/// The product's authorization policies, registered once so every authenticated surface names the
/// same rules: <see cref="TenantUser"/> is any signed-in dashboard user (the dashboard then scopes
/// reads to the session's tenant claim), <see cref="Operator"/> is the operator admin role that may
/// act, and <see cref="Machine"/> is a valid machine API key, whose tenant the management routes act
/// on.
/// </summary>
internal static class CsmsPolicies
{
    public const string TenantUser = "CsmsTenantUser";
    public const string Operator = "CsmsOperator";
    public const string Machine = "CsmsMachine";

    /// <summary>Registers cookie sign-in, the machine API key and the policies the endpoints require.</summary>
    public static IServiceCollection AddCsmsAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "opencsms.session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                options.SlidingExpiration = true;
                // The SPA speaks JSON, not redirects: an anonymous read is 401 and a role refusal is
                // 403, both answered where the API client is looking.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddScheme<AuthenticationSchemeOptions, CsmsApiKeyAuthenticationHandler>(
                CsmsApiKeyAuthentication.Scheme,
                displayName: null,
                configureOptions: null);

        services.AddAuthorization(options =>
        {
            options.AddPolicy(TenantUser, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(UserClaimTypes.TenantId));
            options.AddPolicy(Operator, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(UserClaimTypes.TenantId)
                .RequireRole(UserRoles.Operator));
            // The machine policy authenticates with the API key scheme alone: a dashboard cookie
            // never unlocks the management routes, and the key never unlocks the dashboard.
            options.AddPolicy(Machine, policy => policy
                .AddAuthenticationSchemes(CsmsApiKeyAuthentication.Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(UserClaimTypes.TenantId));
        });

        return services;
    }
}
