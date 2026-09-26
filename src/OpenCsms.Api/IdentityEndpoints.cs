namespace OpenCsms.Api;

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using OpenCsms.Application.Identity;
using OpenCsms.Contracts;
using OpenCsms.Domain;

/// <summary>
/// The product's identity surface: sign-in exchanges email and password for the HttpOnly cookie session
/// the dashboard uses, sign-out clears it, and <c>POST /api/users</c> provisions an account. The
/// management API has no credentials of its own, so user provisioning is part of that same
/// unauthenticated management surface as tariffs and stations; the dashboard itself only ever reads
/// through the cookie. The account rules live in the application; the cookie and the claims live here.
/// </summary>
internal static class IdentityEndpoints
{
    public static void MapIdentityEndpoints(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");
        auth.MapPost("/sign-in", SignInAsync);
        auth.MapPost("/sign-out", SignOutAsync);
        auth.MapGet("/session", GetSession).RequireAuthorization(CsmsPolicies.TenantUser);

        api.MapPost("/users", CreateUserAsync).WithTags("Users");
    }

    private static async Task<IResult> SignInAsync(
        SignInRequest request,
        UserAuthentication authentication,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var user = await authentication.AuthenticateAsync(request.Email, request.Password, cancellationToken);
        if (user is null)
        {
            // One answer for an unknown address and a wrong password, so the surface does not tell an
            // attacker which half to keep guessing.
            return Results.Problem("The email or password is not correct.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(ClaimTypes.Role, UserRoles.From(user.Role)),
                new Claim(UserClaimTypes.TenantId, user.TenantId),
                new Claim(UserClaimTypes.Email, user.Email)
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.Ok(ApiMappings.ToUserSessionResponse(user));
    }

    private static async Task SignOutAsync(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        http.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static IResult GetSession(ClaimsPrincipal user) => Results.Ok(ApiMappings.ToUserSessionResponse(user));

    private static async Task<IResult> CreateUserAsync(
        CreateUserRequest request,
        UserRegistration registration,
        CancellationToken cancellationToken)
    {
        RegisterUserOutcome outcome;
        try
        {
            outcome = await registration.RegisterAsync(
                new RegisterUserCommand(
                    request.TenantId,
                    request.Email,
                    request.DisplayName,
                    request.Password,
                    request.Role),
                cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [exception.ParamName ?? "request"] = [exception.Message]
            });
        }

        return outcome switch
        {
            UserRegistered registered => Results.Created(
                $"/api/users/{registered.User.Id}",
                ApiMappings.ToUserResponse(registered.User)),
            UserRoleUnknown => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Role"] = ["A role is 'operator' or 'viewer'."]
            }),
            UserEmailAlreadyRegistered taken => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Email"] = [$"'{taken.Email}' already has an account."]
            }),
            // Two provisioning calls raced over the same address; the unique index is the truth.
            UserEmailRaceLost race => Results.Conflict(new { message = $"'{race.Email}' already has an account." }),
            _ => throw new InvalidOperationException("Unhandled user registration outcome.")
        };
    }
}
