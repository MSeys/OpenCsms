namespace OpenCsms.Infrastructure.Persistence.Stores;

using Microsoft.EntityFrameworkCore;
using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>The tariff queries and commands, over the one CSMS store.</summary>
public sealed class TariffStore(CsmsDbContext db) : ITariffQueries, ITariffCommands
{
    /// <inheritdoc />
    public Task<Tariff?> FindAsync(Guid tariffId, CancellationToken cancellationToken = default)
        => db.Tariffs.FirstOrDefaultAsync(tariff => tariff.Id == tariffId, cancellationToken);

    /// <inheritdoc />
    public Task<Tariff?> FindForTenantAsync(Guid tariffId, string tenantId, CancellationToken cancellationToken = default)
        => db.Tariffs.FirstOrDefaultAsync(
            tariff => tariff.Id == tariffId && tariff.TenantId == tenantId,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Tariff>> ListAsync(string? tenantId, CancellationToken cancellationToken = default)
        => await db.Tariffs
            .Where(tariff => tenantId == null || tariff.TenantId == tenantId)
            .OrderBy(tariff => tariff.Name)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(Tariff tariff, CancellationToken cancellationToken = default)
    {
        db.Tariffs.Add(tariff);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task SaveAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
