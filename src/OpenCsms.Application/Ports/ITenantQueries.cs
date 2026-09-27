namespace OpenCsms.Application.Ports;

using OpenCsms.Domain;

/// <summary>The tenant reads the machine credential path needs; implemented over the CSMS store.</summary>
public interface ITenantQueries
{
    /// <summary>
    /// Finds the tenant a key hash belongs to, or null when no tenant was registered with that key.
    /// The lookup is by hash, so the presented key never reaches the store.
    /// </summary>
    Task<Tenant?> FindByApiKeyHashAsync(string apiKeyHash, CancellationToken cancellationToken = default);
}
