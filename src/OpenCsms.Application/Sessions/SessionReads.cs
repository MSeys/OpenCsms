namespace OpenCsms.Application.Sessions;

using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>The session read surface: lookups, tenant-scoped existence and a station's newest sessions.</summary>
public sealed class SessionReads(ISessionQueries sessions)
{
    /// <summary>Finds a session by id, or null when none is stored.</summary>
    public Task<ChargingSession?> FindAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => sessions.FindAsync(sessionId, cancellationToken);

    /// <summary>Answers whether the tenant owns a session with the id.</summary>
    public Task<bool> ExistsForTenantAsync(Guid sessionId, string tenantId, CancellationToken cancellationToken = default)
        => sessions.ExistsForTenantAsync(sessionId, tenantId, cancellationToken);

    /// <summary>The station's sessions, newest first.</summary>
    public Task<IReadOnlyList<ChargingSession>> ListByStationAsync(Guid stationId, CancellationToken cancellationToken = default)
        => sessions.ListByStationAsync(stationId, cancellationToken);
}
