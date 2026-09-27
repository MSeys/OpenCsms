namespace OpenCsms.Domain;

/// <summary>
/// A tenant that holds a machine credential: the management API scopes every call to the tenant the
/// key belongs to. The key itself is never stored - only its <see cref="ApiKeyHash"/> - so the raw
/// value exists only in the response that issued it.
/// </summary>
public sealed class Tenant
{
    private const int MaxNameLength = 120;

    private Tenant()
    {
        Id = string.Empty;
        Name = string.Empty;
        ApiKeyHash = string.Empty;
    }

    private Tenant(string id, string name, string apiKeyHash, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        ApiKeyHash = apiKeyHash;
        CreatedAtUtc = createdAtUtc;
    }

    public string Id { get; private set; }

    public string Name { get; private set; }

    public string ApiKeyHash { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Registers a tenant whose key hash was computed from a fresh key the caller holds.</summary>
    public static Tenant Create(string name, string apiKeyHash, DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKeyHash);
        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
        {
            throw new ArgumentException($"A tenant name is {MaxNameLength} characters or fewer.", nameof(name));
        }

        return new Tenant(Guid.NewGuid().ToString(), trimmed, apiKeyHash, createdAtUtc);
    }
}
