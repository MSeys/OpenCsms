namespace OpenCsms.Suite.Devices;

/// <summary>
/// One transaction the simulator started through <see cref="AcCharger.PlugInAsync"/>: the connector,
/// id tag and CSMS-assigned transaction number, and the meter register the connector stood at.
/// </summary>
public sealed class AcChargerSession
{
    internal AcChargerSession(int connectorId, string idTag, int transactionId, int meterStartWh)
    {
        ConnectorId = connectorId;
        IdTag = idTag;
        TransactionId = transactionId;
        MeterStartWh = meterStartWh;
    }

    /// <summary>The connector the transaction runs on.</summary>
    public int ConnectorId { get; }

    /// <summary>The id tag that plugged in.</summary>
    public string IdTag { get; }

    /// <summary>The transaction number the CSMS assigned in StartTransaction.conf.</summary>
    public int TransactionId { get; }

    /// <summary>The connector's meter register at plug-in, in watt-hours.</summary>
    public int MeterStartWh { get; }
}
