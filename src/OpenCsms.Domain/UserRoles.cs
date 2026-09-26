namespace OpenCsms.Domain;

/// <summary>The roles a dashboard user can hold: an operator runs the network, a viewer reads it.</summary>
public enum UserRole
{
    Operator,
    Viewer
}

/// <summary>
/// The wire, claim and storage spellings of <see cref="UserRole"/>, so the API, the SPA and the suite
/// share one vocabulary. Roles are lower case everywhere they cross a boundary.
/// </summary>
public static class UserRoles
{
    public const string Operator = "operator";
    public const string Viewer = "viewer";

    public static string From(UserRole role) => role switch
    {
        UserRole.Operator => Operator,
        UserRole.Viewer => Viewer,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role.")
    };

    public static UserRole Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Operator => UserRole.Operator,
        Viewer => UserRole.Viewer,
        _ => throw new ArgumentException("A role is 'operator' or 'viewer'.", nameof(value))
    };

    public static bool IsKnown(string? value) => value?.Trim().ToLowerInvariant() is Operator or Viewer;
}

/// <summary>The claims a product session carries beyond the standard identity claims.</summary>
public static class UserClaimTypes
{
    /// <summary>The tenant the session is scoped to; the dashboard never trusts a tenant from the request.</summary>
    public const string TenantId = "opencsms:tenant";

    /// <summary>The sign-in address the cookie was issued for.</summary>
    public const string Email = "opencsms:email";
}
