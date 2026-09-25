namespace OpenCsms.Domain;

/// <summary>
/// One charging session: started at a station connector, fed meter values in time order, ended by a
/// stop transaction. <see cref="EnergyKwh"/> is the last meter reading, in kilowatt-hours.
/// </summary>
public sealed class ChargingSession
{
    private ChargingSession()
    {
        TenantId = string.Empty;
    }

    private ChargingSession(Guid id, string tenantId, Guid stationId, int connectorId, DateTimeOffset startedAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        StationId = stationId;
        ConnectorId = connectorId;
        StartedAtUtc = startedAtUtc;
        LastMeterAtUtc = startedAtUtc;
    }

    public Guid Id { get; private set; }

    public string TenantId { get; private set; }

    public Guid StationId { get; private set; }

    public int ConnectorId { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset? EndedAtUtc { get; private set; }

    public decimal EnergyKwh { get; private set; }

    /// <summary>When the last meter value arrived; idle time is measured from here, not from the start.</summary>
    public DateTimeOffset LastMeterAtUtc { get; private set; }

    public bool IsOpen => EndedAtUtc is null;

    public static ChargingSession Start(string tenantId, Guid stationId, int connectorId, DateTimeOffset startedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (stationId == Guid.Empty)
        {
            throw new ArgumentException("A session needs a station.", nameof(stationId));
        }

        if (connectorId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(connectorId), connectorId, "Connectors are numbered from one.");
        }

        return new ChargingSession(Guid.NewGuid(), tenantId, stationId, connectorId, startedAtUtc);
    }

    /// <summary>
    /// Records a cumulative meter reading. Readings must not move backwards in time or energy; the
    /// device is allowed to repeat the same total.
    /// </summary>
    public void RecordMeter(DateTimeOffset atUtc, decimal totalKwh)
    {
        EnsureOpen("Meter values cannot arrive after the stop transaction.");
        if (totalKwh < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(totalKwh), totalKwh, "A meter cannot read negative energy.");
        }

        if (totalKwh < EnergyKwh)
        {
            throw new ArgumentOutOfRangeException(nameof(totalKwh), totalKwh, "A meter reading cannot move backwards.");
        }

        if (atUtc < LastMeterAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(atUtc), atUtc, "Meter values arrive in time order.");
        }

        EnergyKwh = totalKwh;
        LastMeterAtUtc = atUtc;
    }

    public void End(DateTimeOffset atUtc)
    {
        EnsureOpen("The session has already ended.");
        if (atUtc < LastMeterAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(atUtc), atUtc, "A session cannot end before its last meter value.");
        }

        EndedAtUtc = atUtc;
    }

    private void EnsureOpen(string message)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException(message);
        }
    }
}
