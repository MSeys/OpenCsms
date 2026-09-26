namespace OpenCsms.Domain;

/// <summary>
/// The last status a charge point reported for one connector, kept so an operator can see the
/// device edge's view of the station. Connector 0 is the charge point itself, the OCPP convention.
/// </summary>
public sealed class Connector
{
    private Connector()
    {
    }

    private Connector(
        Guid stationId,
        int connectorId,
        ConnectorStatus status,
        ConnectorErrorCode errorCode,
        DateTimeOffset updatedAtUtc)
    {
        StationId = stationId;
        ConnectorId = connectorId;
        Status = status;
        ErrorCode = errorCode;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid StationId { get; private set; }

    /// <summary>The connector number, 1..connectorCount, or 0 for the charge point's own status.</summary>
    public int ConnectorId { get; private set; }

    public ConnectorStatus Status { get; private set; }

    public ConnectorErrorCode ErrorCode { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Connector Report(
        Guid stationId,
        int connectorId,
        ConnectorStatus status,
        ConnectorErrorCode errorCode,
        DateTimeOffset updatedAtUtc)
    {
        if (stationId == Guid.Empty)
        {
            throw new ArgumentException("A connector belongs to a station.", nameof(stationId));
        }

        if (connectorId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(connectorId), connectorId, "Connector numbers start at zero.");
        }

        return new Connector(stationId, connectorId, status, errorCode, updatedAtUtc);
    }

    /// <summary>
    /// Applies a later status notification from the same connector: the reported state replaces the
    /// previous one. The station owns which connector numbers exist; the caller only reports what it
    /// received.
    /// </summary>
    public void Update(ConnectorStatus status, ConnectorErrorCode errorCode, DateTimeOffset updatedAtUtc)
    {
        Status = status;
        ErrorCode = errorCode;
        UpdatedAtUtc = updatedAtUtc;
    }
}
