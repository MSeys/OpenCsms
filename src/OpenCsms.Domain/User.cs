namespace OpenCsms.Domain;

using System.Globalization;
using System.Security.Cryptography;

/// <summary>
/// A person who signs in to the operator dashboard: one tenant, one role, and a password stored as a
/// salted PBKDF2-SHA256 hash so the plaintext never leaves the request that created it.
/// </summary>
public sealed class User
{
    private const string HashPrefix = "pbkdf2-sha256";
    private const int HashIterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int MaxEmailLength = 256;
    private const int MaxDisplayNameLength = 120;

    private User()
    {
        TenantId = string.Empty;
        Email = string.Empty;
        DisplayName = string.Empty;
        PasswordHash = string.Empty;
    }

    private User(
        Guid id,
        string tenantId,
        string email,
        string displayName,
        string passwordHash,
        UserRole role,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        Email = email;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        Role = role;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    /// <summary>The operator this user belongs to; a dashboard session never reads another tenant's rows.</summary>
    public string TenantId { get; private set; }

    /// <summary>The sign-in address, normalized to lower case so the account store cannot hold two spellings of it.</summary>
    public string Email { get; private set; }

    public string DisplayName { get; private set; }

    public string PasswordHash { get; private set; }

    public UserRole Role { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>An account with a normalized sign-in address, a role and the instant it was created.</summary>
    public static User Create(
        string tenantId,
        string email,
        string displayName,
        string password,
        UserRole role,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (normalizedEmail.Length > MaxEmailLength || !normalizedEmail.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("A sign-in address is an email address.", nameof(email));
        }

        var name = displayName.Trim();
        if (name.Length > MaxDisplayNameLength)
        {
            throw new ArgumentException($"A display name is {MaxDisplayNameLength} characters or fewer.", nameof(displayName));
        }

        if (password.Length < 8)
        {
            throw new ArgumentException("A password is at least eight characters.", nameof(password));
        }

        return new User(Guid.NewGuid(), tenantId, normalizedEmail, name, Hash(password), role, createdAtUtc);
    }

    /// <summary>Checks a sign-in attempt against the stored hash in constant time.</summary>
    public bool VerifyPassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        var parts = PasswordHash.Split('$');
        if (parts.Length != 4
            || !string.Equals(parts[0], HashPrefix, StringComparison.Ordinal)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, HashIterations, HashAlgorithmName.SHA256, HashBytes);
        return string.Join(
            '$',
            HashPrefix,
            HashIterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }
}
