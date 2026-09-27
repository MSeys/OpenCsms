namespace OpenCsms.Infrastructure.Persistence.Stores;

using Microsoft.EntityFrameworkCore;
using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>The tenant queries and commands, over the one CSMS store.</summary>
public sealed class TenantStore(CsmsDbContext db) : ITenantQueries, ITenantCommands
{
    /// <inheritdoc />
    public Task<Tenant?> FindByApiKeyHashAsync(string apiKeyHash, CancellationToken cancellationToken = default)
        => db.Tenants.FirstOrDefaultAsync(tenant => tenant.ApiKeyHash == apiKeyHash, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);
    }
}
