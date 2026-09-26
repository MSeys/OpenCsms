namespace OpenCsms.Application.ChargePoints;

using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>
/// Applies a connector status notification a charge point sent: the station's own rule decides which
/// connector numbers exist, the connector row is created on its first report and updated afterwards,
/// and the station's last-seen stamp moves with it. The caller resolved the station for this charge
/// point; the connector's own rules live on the aggregate.
/// </summary>
public sealed class ConnectorStatusReport(
    IConnectorQueries connectorQueries,
    IConnectorCommands connectorCommands,
    TimeProvider clock)
{
    /// <summary>Upserts the connector's status; the reported values are the domain's own.</summary>
    public async Task<ConnectorReportOutcome> ReportAsync(
        Station station,
        int connectorId,
        ConnectorStatus status,
        ConnectorErrorCode errorCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(station);
        if (!station.CanReportStatus(connectorId))
        {
            return new ConnectorOutsideStation(station, connectorId);
        }

        var now = clock.GetUtcNow();
        var existing = await connectorQueries.FindAsync(station.Id, connectorId, cancellationToken);
        var connector = station.UpsertConnector(existing, connectorId, status, errorCode, now);
        station.MarkSeen(now);
        if (existing is null)
        {
            await connectorCommands.AddAsync(connector, cancellationToken);
        }
        else
        {
            await connectorCommands.SaveAsync(cancellationToken);
        }

        return new ConnectorStatusRecorded(connector);
    }
}

/// <summary>What a connector status notification ended in; the caller maps each to its own answer.</summary>
public abstract record ConnectorReportOutcome;

/// <summary>The connector is stored with the reported status and the station is marked seen.</summary>
public sealed record ConnectorStatusRecorded(Connector Connector) : ConnectorReportOutcome;

/// <summary>The station exists but has no such connector; the caller explains the station's size.</summary>
public sealed record ConnectorOutsideStation(Station Station, int ConnectorId) : ConnectorReportOutcome;
