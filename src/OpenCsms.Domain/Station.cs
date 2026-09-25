namespace OpenCsms.Domain;

/// <summary>A charge point an operator owns: one tariff, one or more connectors.</summary>
public sealed class Station
{
    private Station()
    {
        TenantId = string.Empty;
        Name = string.Empty;
    }

    private Station(Guid id, string tenantId, string name, int connectorCount, Guid tariffId)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        ConnectorCount = connectorCount;
        TariffId = tariffId;
    }

    public Guid Id { get; private set; }

    /// <summary>The operator this station belongs to; data must never leak across tenants.</summary>
    public string TenantId { get; private set; }

    public string Name { get; private set; }

    public int ConnectorCount { get; private set; }

    public Guid TariffId { get; private set; }

    public static Station Register(string tenantId, string name, int connectorCount, Guid tariffId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (connectorCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(connectorCount), connectorCount, "A station has at least one connector.");
        }

        if (tariffId == Guid.Empty)
        {
            throw new ArgumentException("A station needs a tariff.", nameof(tariffId));
        }

        return new Station(Guid.NewGuid(), tenantId, name, connectorCount, tariffId);
    }
}
