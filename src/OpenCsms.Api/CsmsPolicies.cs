namespace OpenCsms.Api;

using OpenCsms.Domain;
using Microsoft.AspNetCore.Authentication.Cookies;

/// <summary>
/// The product's two authorization policies, registered once so every authenticated surface names the
/// same rules: <see cref="TenantUser"/> is any signed-in user (the dashboard then scopes reads to the
/// session's tenant claim), <see cref="Operator"/> is the operator admin role that may act.
/// </summary>
internal static class CsmsPolicies
{
    public const string TenantUser = "CsmsTenantUser";
    public const string Operator = "CsmsOperator";

    /// <summary>Registers cookie sign-in and the policies the dashboard's endpoints require.</summary>
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
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(TenantUser, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(UserClaimTypes.TenantId));
            options.AddPolicy(Operator, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(UserClaimTypes.TenantId)
                .RequireRole(UserRoles.Operator));
        });

        return services;
    }
}
