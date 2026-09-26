namespace OpenCsms.Application.Ports;

using OpenCsms.Domain;

/// <summary>The charging-session reads a use case needs; implemented over the CSMS store.</summary>
public interface ISessionQueries
{
    /// <summary>Finds a session by id, or null when none is stored.</summary>
    Task<ChargingSession?> FindAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Finds the open session on a station connector, or null when the connector is idle.</summary>
    Task<ChargingSession?> FindOpenAsync(Guid stationId, int connectorId, CancellationToken cancellationToken = default);

    /// <summary>Finds a session by the transaction number the CSMS assigned the charge point.</summary>
    Task<ChargingSession?> FindByTransactionAsync(Guid stationId, int transactionId, CancellationToken cancellationToken = default);

    /// <summary>Answers whether the tenant owns a session with the id.</summary>
    Task<bool> ExistsForTenantAsync(Guid sessionId, string tenantId, CancellationToken cancellationToken = default);

    /// <summary>The station's sessions, newest first.</summary>
    Task<IReadOnlyList<ChargingSession>> ListByStationAsync(Guid stationId, CancellationToken cancellationToken = default);
}
