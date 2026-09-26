namespace OpenCsms.Application.Ports;

using OpenCsms.Domain;

/// <summary>The connector reads a use case needs; implemented over the CSMS store.</summary>
public interface IConnectorQueries
{
    /// <summary>Finds one connector of a station, or null when the station never reported it.</summary>
    Task<Connector?> FindAsync(Guid stationId, int connectorId, CancellationToken cancellationToken = default);

    /// <summary>The station's connectors, by connector number.</summary>
    Task<IReadOnlyList<Connector>> ListByStationAsync(Guid stationId, CancellationToken cancellationToken = default);

    /// <summary>Every connector of every station, by connector number; the public status page's view.</summary>
    Task<IReadOnlyList<Connector>> ListAllAsync(CancellationToken cancellationToken = default);
}
