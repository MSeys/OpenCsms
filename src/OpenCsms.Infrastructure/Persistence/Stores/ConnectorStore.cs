namespace OpenCsms.Infrastructure.Persistence.Stores;

using Microsoft.EntityFrameworkCore;
using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>The connector queries and commands, over the one CSMS store.</summary>
public sealed class ConnectorStore(CsmsDbContext db) : IConnectorQueries, IConnectorCommands
{
    /// <inheritdoc />
    public Task<Connector?> FindAsync(Guid stationId, int connectorId, CancellationToken cancellationToken = default)
        => db.Connectors.FirstOrDefaultAsync(
            connector => connector.StationId == stationId && connector.ConnectorId == connectorId,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Connector>> ListByStationAsync(Guid stationId, CancellationToken cancellationToken = default)
        => await db.Connectors
            .Where(connector => connector.StationId == stationId)
            .OrderBy(connector => connector.ConnectorId)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Connector>> ListAllAsync(CancellationToken cancellationToken = default)
        => await db.Connectors
            .OrderBy(connector => connector.ConnectorId)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(Connector connector, CancellationToken cancellationToken = default)
    {
        db.Connectors.Add(connector);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task SaveAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
