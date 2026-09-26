namespace OpenCsms.Domain;

/// <summary>
/// One charging session: started at a station connector, fed meter values in time order, ended by a
/// stop transaction. A real connector's meter is a register that keeps counting across sessions, so
/// the session remembers <see cref="MeterStartKwh"/> and <see cref="EnergyKwh"/> is only what this
/// session added on top of it.
/// </summary>
public sealed class ChargingSession
{
    private ChargingSession()
    {
        TenantId = string.Empty;
    }

    private ChargingSession(
        Guid id,
        string tenantId,
        Guid stationId,
        int connectorId,
        DateTimeOffset startedAtUtc,
        decimal meterStartKwh)
    {
        Id = id;
        TenantId = tenantId;
        StationId = stationId;
        ConnectorId = connectorId;
        StartedAtUtc = startedAtUtc;
        LastMeterAtUtc = startedAtUtc;
        MeterStartKwh = meterStartKwh;
    }

    public Guid Id { get; private set; }

    /// <summary>The CSMS-assigned transaction number the charge point uses in MeterValues and StopTransaction.</summary>
    public int TransactionId { get; private set; }

    public string TenantId { get; private set; }

    public Guid StationId { get; private set; }

    public int ConnectorId { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset? EndedAtUtc { get; private set; }

    /// <summary>The connector's meter register when this session started, in kilowatt-hours.</summary>
    public decimal MeterStartKwh { get; private set; }

    /// <summary>The energy this session delivered: the last reading minus <see cref="MeterStartKwh"/>.</summary>
    public decimal EnergyKwh { get; private set; }

    /// <summary>The connector's last cumulative reading; the register the charge point reports.</summary>
    public decimal LastReadingKwh => MeterStartKwh + EnergyKwh;

    /// <summary>When the last meter value arrived; idle time is measured from here, not from the start.</summary>
    public DateTimeOffset LastMeterAtUtc { get; private set; }

    public bool IsOpen => EndedAtUtc is null;

    /// <summary>
    /// Starts a session at the connector's current meter register. The register is the baseline every
    /// later reading is billed against, so a second session on the same connector bills its own energy
    /// only.
    /// </summary>
    public static ChargingSession Start(
        string tenantId,
        Guid stationId,
        int connectorId,
        DateTimeOffset startedAtUtc,
        decimal meterStartKwh = 0m)
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

        if (meterStartKwh < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(meterStartKwh),
                meterStartKwh,
                "A meter cannot start at a negative reading.");
        }

        return new ChargingSession(Guid.NewGuid(), tenantId, stationId, connectorId, startedAtUtc, meterStartKwh);
    }

    /// <summary>
    /// Records a cumulative meter reading. Readings must not move backwards in time or energy, must not
    /// fall below the session's start reading, and the device is allowed to repeat the same total.
    /// </summary>
    public void RecordMeter(DateTimeOffset atUtc, decimal totalKwh)
    {
        EnsureOpen("Meter values cannot arrive after the stop transaction.");
        if (totalKwh < MeterStartKwh)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalKwh),
                totalKwh,
                $"A meter reading cannot be below the session's start reading of {MeterStartKwh} kWh.");
        }

        if (totalKwh < LastReadingKwh)
        {
            throw new ArgumentOutOfRangeException(nameof(totalKwh), totalKwh, "A meter reading cannot move backwards.");
        }

        if (atUtc < LastMeterAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(atUtc), atUtc, "Meter values arrive in time order.");
        }

        EnergyKwh = totalKwh - MeterStartKwh;
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
