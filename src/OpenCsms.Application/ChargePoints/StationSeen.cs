namespace OpenCsms.Application.ChargePoints;

using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>
/// Reconciles a charge point that booted or sent a heartbeat: the station registered for this OCPP
/// identity moves its last-seen stamp, and an identity no station carries is reported back so each
/// caller answers its own way - a boot is rejected, every other call refused. The station's own
/// operation stamps the instant the application clock gives.
/// </summary>
public sealed class StationSeen(
    IStationQueries stations,
    IStationCommands stationCommands,
    TimeProvider clock)
{
    /// <summary>Stamps the station seen, or reports that no station carries the identity.</summary>
    public async Task<StationSeenOutcome> RecordAsync(
        string chargePointId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chargePointId);
        var station = await stations.FindByChargePointIdAsync(chargePointId, cancellationToken);
        if (station is null)
        {
            return new StationSeenUnknown(chargePointId);
        }

        station.MarkSeen(clock.GetUtcNow());
        await stationCommands.SaveAsync(cancellationToken);
        return new StationSeenRecorded(station);
    }
}

/// <summary>What reconciling a charge point ended in; the caller maps each to its own answer.</summary>
public abstract record StationSeenOutcome;

/// <summary>The station is registered for the identity and its last-seen stamp moved.</summary>
public sealed record StationSeenRecorded(Station Station) : StationSeenOutcome;

/// <summary>No station carries the OCPP identity; the charge point was never registered.</summary>
public sealed record StationSeenUnknown(string ChargePointId) : StationSeenOutcome;
