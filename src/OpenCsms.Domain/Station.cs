namespace OpenCsms.Domain;

/// <summary>A charge point an operator owns: one tariff, one or more connectors, one OCPP identity.</summary>
public sealed class Station
{
    private Station()
    {
        TenantId = string.Empty;
        ChargePointId = string.Empty;
        Name = string.Empty;
    }

    private Station(Guid id, string tenantId, string chargePointId, string name, int connectorCount, Guid tariffId)
    {
        Id = id;
        TenantId = tenantId;
        ChargePointId = chargePointId;
        Name = name;
        ConnectorCount = connectorCount;
        TariffId = tariffId;
    }

    public Guid Id { get; private set; }

    /// <summary>The operator this station belongs to; data must never leak across tenants.</summary>
    public string TenantId { get; private set; }

    /// <summary>The OCPP identity the charge point boots with; unique across operators.</summary>
    public string ChargePointId { get; private set; }

    public string Name { get; private set; }

    public int ConnectorCount { get; private set; }

    public Guid TariffId { get; private set; }

    /// <summary>When the charge point last booted or sent a heartbeat; null until it first connects.</summary>
    public DateTimeOffset? LastSeenAtUtc { get; private set; }

    public static Station Register(string tenantId, string chargePointId, string name, int connectorCount, Guid tariffId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(chargePointId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (connectorCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(connectorCount), connectorCount, "A station has at least one connector.");
        }

        if (tariffId == Guid.Empty)
        {
            throw new ArgumentException("A station needs a tariff.", nameof(tariffId));
        }

        return new Station(Guid.NewGuid(), tenantId, chargePointId, name, connectorCount, tariffId);
    }

    /// <summary>Records that the charge point is alive, from a boot notification or a heartbeat.</summary>
    public void MarkSeen(DateTimeOffset atUtc) => LastSeenAtUtc = atUtc;

    /// <summary>Whether the connector number is one the station reports on: 0 is the charge point itself.</summary>
    public bool CanReportStatus(int connectorId) => connectorId >= 0 && connectorId <= ConnectorCount;

    /// <summary>Whether the connector number can carry a transaction: connectors are numbered from one.</summary>
    public bool CanServeTransaction(int connectorId) => connectorId >= 1 && connectorId <= ConnectorCount;

    /// <summary>
    /// Applies a status notification: the station owns which connector numbers exist, so an existing
    /// connector of this station is updated and a missing one is created. The connector is a stored row
    /// of its own; the caller loads it and passes it in so the aggregate stays persistence-free.
    /// </summary>
    public Connector UpsertConnector(
        Connector? connector,
        int connectorId,
        ConnectorStatus status,
        ConnectorErrorCode errorCode,
        DateTimeOffset atUtc)
    {
        if (!CanReportStatus(connectorId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(connectorId),
                connectorId,
                $"Station '{Name}' has {ConnectorCount} connector(s).");
        }

        if (connector is not null && connector.StationId != Id)
        {
            throw new ArgumentException("The connector belongs to another station.", nameof(connector));
        }

        if (connector is null)
        {
            return Connector.Report(Id, connectorId, status, errorCode, atUtc);
        }

        connector.Update(status, errorCode, atUtc);
        return connector;
    }
}
