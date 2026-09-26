namespace OpenCsms.Api;

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using OpenCsms.Data;
using OpenCsms.Domain;

/// <summary>
/// The product's identity surface: sign-in exchanges email and password for the HttpOnly cookie session
/// the dashboard uses, sign-out clears it, and <c>POST /api/users</c> provisions an account. Until M4
/// gives the management API credentials of its own, user provisioning is part of that same
/// unauthenticated management surface as tariffs and stations; the dashboard itself only ever reads
/// through the cookie.
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
        CsmsDbContext db,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim().ToLowerInvariant();
        User? user = null;
        if (!string.IsNullOrEmpty(email))
        {
            user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);
        }

        if (user is null || !user.VerifyPassword(request.Password))
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
        return Results.Ok(UserSessionResponse.From(user));
    }

    private static async Task SignOutAsync(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        http.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static IResult GetSession(ClaimsPrincipal user) => Results.Ok(UserSessionResponse.FromPrincipal(user));

    private static async Task<IResult> CreateUserAsync(
        CreateUserRequest request,
        CsmsDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!UserRoles.IsKnown(request.Role))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Role"] = ["A role is 'operator' or 'viewer'."]
            });
        }

        User user;
        try
        {
            user = User.Create(
                request.TenantId ?? string.Empty,
                request.Email ?? string.Empty,
                request.DisplayName ?? string.Empty,
                request.Password ?? string.Empty,
                UserRoles.Parse(request.Role),
                clock.GetUtcNow());
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [exception.ParamName ?? "request"] = [exception.Message]
            });
        }

        if (await db.Users.AnyAsync(candidate => candidate.Email == user.Email, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Email"] = [$"'{user.Email}' already has an account."]
            });
        }

        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two provisioning calls raced over the same address; the unique index is the truth.
            return Results.Conflict(new { message = $"'{user.Email}' already has an account." });
        }

        return Results.Created($"/api/users/{user.Id}", UserResponse.From(user));
    }
}
