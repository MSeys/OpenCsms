namespace OpenCsms.Application.Commands;

using Microsoft.Extensions.Options;
using OpenCsms.Application.Ports;

/// <summary>
/// The operator commands that reach a connected charge point. One implementation serves the machine
/// API and the signed-in dashboard, which adds the operator role and the tenant scope in front of the
/// same code, so a fix reaches both doors. The state checks, the configured timeout and the outcome
/// mapping live here; the connection registry is a port the transport implements, and the caller maps
/// each outcome to its own status code.
/// </summary>
public sealed class OperatorCommands(
    IStationQueries stations,
    ISessionQueries sessions,
    IChargePointConnections connections,
    IOptions<RemoteCommandOptions> options)
{
    /// <summary>
    /// The operator asks a connected charge point to start a transaction. The session itself starts
    /// when the charge point sends its StartTransaction, exactly as OCPP prescribes.
    /// </summary>
    public async Task<RemoteCommandOutcome> RemoteStartAsync(
        Guid stationId,
        string idTag,
        int? connectorId,
        CancellationToken cancellationToken = default)
    {
        var station = await stations.FindAsync(stationId, cancellationToken);
        if (station is null)
        {
            return new RemoteCommandStationMissing(stationId);
        }

        if (!connections.TryGet(station.ChargePointId, out var connection))
        {
            return new RemoteCommandChargePointOffline(station.ChargePointId);
        }

        try
        {
            var status = await connection.RemoteStartAsync(idTag, connectorId, Timeout, cancellationToken);
            return new RemoteCommandAccepted(status);
        }
        catch (ChargePointConnectionLostException)
        {
            // The connection the call travelled died - typically a reconnect replacing it - so the
            // charge point is unreachable for this call; the operator retries, which reaches the
            // live connection.
            return new RemoteCommandChargePointOffline(station.ChargePointId);
        }
        catch (TimeoutException exception)
        {
            return new RemoteCommandTimedOut(exception.Message);
        }
        catch (ChargePointCallRefusedException exception)
        {
            return new RemoteCommandRefused(exception.Message);
        }
    }

    /// <summary>The operator asks a connected charge point to stop the transaction of a session.</summary>
    public async Task<RemoteCommandOutcome> RemoteStopAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await sessions.FindAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return new RemoteCommandSessionMissing(sessionId);
        }

        if (!session.IsOpen)
        {
            return new RemoteCommandSessionEnded(sessionId);
        }

        var station = await stations.FindAsync(session.StationId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Session '{sessionId}' references an unknown station '{session.StationId}'.");
        if (!connections.TryGet(station.ChargePointId, out var connection))
        {
            return new RemoteCommandChargePointOffline(station.ChargePointId);
        }

        try
        {
            var status = await connection.RemoteStopAsync(session.TransactionId, Timeout, cancellationToken);
            return new RemoteCommandAccepted(status);
        }
        catch (ChargePointConnectionLostException)
        {
            // A lost connection leaves the session as it was; the operator retries against the live
            // connection.
            return new RemoteCommandChargePointOffline(station.ChargePointId);
        }
        catch (TimeoutException exception)
        {
            return new RemoteCommandTimedOut(exception.Message);
        }
        catch (ChargePointCallRefusedException exception)
        {
            return new RemoteCommandRefused(exception.Message);
        }
    }

    private TimeSpan Timeout => TimeSpan.FromSeconds(options.Value.RemoteCallTimeoutSeconds);
}
