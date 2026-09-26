namespace OpenCsms.Application.Identity;

using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>
/// Provisions a dashboard account. The role vocabulary, the address uniqueness and the domain's
/// factory rules are the use case's checks; a provisioning race is answered as a lost race, because
/// the store's unique index is the truth. The caller answers each outcome in its own shape.
/// </summary>
public sealed class UserRegistration(IUserQueries users, IUserCommands commands, TimeProvider clock)
{
    public async Task<RegisterUserOutcome> RegisterAsync(
        RegisterUserCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!UserRoles.IsKnown(command.Role))
        {
            return new UserRoleUnknown();
        }

        var user = User.Create(
            command.TenantId ?? string.Empty,
            command.Email ?? string.Empty,
            command.DisplayName ?? string.Empty,
            command.Password ?? string.Empty,
            UserRoles.Parse(command.Role),
            clock.GetUtcNow());
        if (await users.ExistsAsync(user.Email, cancellationToken))
        {
            return new UserEmailAlreadyRegistered(user.Email);
        }

        return await commands.TryAddAsync(user, cancellationToken)
            ? new UserRegistered(user)
            : new UserEmailRaceLost(user.Email);
    }
}

/// <summary>The fields an account provisioning carries.</summary>
public sealed record RegisterUserCommand(
    string? TenantId,
    string? Email,
    string? DisplayName,
    string? Password,
    string? Role);

/// <summary>What provisioning an account ended in; the caller maps each to its own answer.</summary>
public abstract record RegisterUserOutcome;

/// <summary>The account is stored.</summary>
public sealed record UserRegistered(User User) : RegisterUserOutcome;

/// <summary>The role is neither 'operator' nor 'viewer'.</summary>
public sealed record UserRoleUnknown : RegisterUserOutcome;

/// <summary>The sign-in address already has an account.</summary>
public sealed record UserEmailAlreadyRegistered(string Email) : RegisterUserOutcome;

/// <summary>Another provisioning call stored the address first; the unique index refused this one.</summary>
public sealed record UserEmailRaceLost(string Email) : RegisterUserOutcome;
