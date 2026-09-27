namespace OpenCsms.Application.Identity;

using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>
/// Resolves the tenant a presented machine key belongs to. Null is the one answer for a missing key
/// and an unknown one alike; the authentication scheme answers the 401.
/// </summary>
public sealed class TenantAuthentication(ITenantQueries tenants)
{
    public Task<Tenant?> AuthenticateAsync(
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Task.FromResult<Tenant?>(null);
        }

        return tenants.FindByApiKeyHashAsync(MachineApiKey.Hash(apiKey), cancellationToken);
    }
}
