namespace OpenCsms.Infrastructure.Persistence.Stores;

using Microsoft.EntityFrameworkCore;
using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>The station queries and commands, over the one CSMS store.</summary>
public sealed class StationStore(CsmsDbContext db) : IStationQueries, IStationCommands
{
    /// <inheritdoc />
    public Task<Station?> FindAsync(Guid stationId, CancellationToken cancellationToken = default)
        => db.Stations.FirstOrDefaultAsync(station => station.Id == stationId, cancellationToken);

    /// <inheritdoc />
    public Task<Station?> FindByChargePointIdAsync(string chargePointId, CancellationToken cancellationToken = default)
        => db.Stations.FirstOrDefaultAsync(station => station.ChargePointId == chargePointId, cancellationToken);

    /// <inheritdoc />
    public Task<Station?> FindForTenantAsync(Guid stationId, string tenantId, CancellationToken cancellationToken = default)
        => db.Stations.FirstOrDefaultAsync(
            station => station.Id == stationId && station.TenantId == tenantId,
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(Guid stationId, CancellationToken cancellationToken = default)
        => db.Stations.AnyAsync(station => station.Id == stationId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsForTenantAsync(Guid stationId, string tenantId, CancellationToken cancellationToken = default)
        => db.Stations.AnyAsync(station => station.Id == stationId && station.TenantId == tenantId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Station>> ListAsync(string? tenantId, CancellationToken cancellationToken = default)
        => await db.Stations
            .Where(station => tenantId == null || station.TenantId == tenantId)
            .OrderBy(station => station.Name)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(Station station, CancellationToken cancellationToken = default)
    {
        db.Stations.Add(station);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task SaveAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
