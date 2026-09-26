namespace OpenCsms.Infrastructure.Persistence.Stores;

using Microsoft.EntityFrameworkCore;
using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>The charging-session queries and commands, over the one CSMS store.</summary>
public sealed class SessionStore(CsmsDbContext db) : ISessionQueries, ISessionCommands
{
    /// <inheritdoc />
    public Task<ChargingSession?> FindAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => db.Sessions.FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken);

    /// <inheritdoc />
    public Task<ChargingSession?> FindOpenAsync(Guid stationId, int connectorId, CancellationToken cancellationToken = default)
        => db.Sessions.FirstOrDefaultAsync(
            session => session.StationId == stationId
                && session.ConnectorId == connectorId
                && session.EndedAtUtc == null,
            cancellationToken);

    /// <inheritdoc />
    public Task<ChargingSession?> FindByTransactionAsync(
        Guid stationId,
        int transactionId,
        CancellationToken cancellationToken = default)
        => db.Sessions.FirstOrDefaultAsync(
            session => session.TransactionId == transactionId && session.StationId == stationId,
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsForTenantAsync(Guid sessionId, string tenantId, CancellationToken cancellationToken = default)
        => db.Sessions.AnyAsync(session => session.Id == sessionId && session.TenantId == tenantId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChargingSession>> ListByStationAsync(Guid stationId, CancellationToken cancellationToken = default)
        => await db.Sessions
            .Where(session => session.StationId == stationId)
            .OrderByDescending(session => session.StartedAtUtc)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(ChargingSession session, CancellationToken cancellationToken = default)
    {
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task SaveAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
