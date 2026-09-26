namespace OpenCsms.Application.Identity;

using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>
/// Checks a sign-in attempt against the account store. Null is the one answer for an unknown address
/// and a wrong password alike, so the surface does not tell an attacker which half to keep guessing;
/// the cookie itself is the presentation's business.
/// </summary>
public sealed class UserAuthentication(IUserQueries users)
{
    public async Task<User?> AuthenticateAsync(
        string? email,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var normalized = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        var user = await users.FindByEmailAsync(normalized, cancellationToken);
        return user is not null && user.VerifyPassword(password) ? user : null;
    }
}
