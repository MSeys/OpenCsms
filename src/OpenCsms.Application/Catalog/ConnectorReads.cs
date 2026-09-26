namespace OpenCsms.Application.Catalog;

using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>The connector read surface: one station's connectors, or every connector for the public page.</summary>
public sealed class ConnectorReads(IConnectorQueries connectors)
{
    public Task<IReadOnlyList<Connector>> ListByStationAsync(Guid stationId, CancellationToken cancellationToken = default)
        => connectors.ListByStationAsync(stationId, cancellationToken);

    public Task<IReadOnlyList<Connector>> ListAllAsync(CancellationToken cancellationToken = default)
        => connectors.ListAllAsync(cancellationToken);
}
