namespace OpenCsms.Application.ChargePoints;

using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>
/// Opens the session a charge point's StartTransaction asks for: the connector must be one the
/// station serves and the id tag must be present; a connector that is already charging points the
/// charge point at the running transaction instead of opening a second one. The reported register is
/// the new session's baseline, so a second session on the connector bills only what it adds.
/// </summary>
public sealed class TransactionStart(
    ISessionQueries sessionQueries,
    ISessionCommands sessionCommands,
    TimeProvider clock)
{
    /// <summary>Starts the transaction, or reports the state that refused it.</summary>
    public async Task<TransactionStartOutcome> StartAsync(
        Station station,
        int connectorId,
        string idTag,
        decimal meterStartKwh,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(station);
        if (!station.CanServeTransaction(connectorId))
        {
            return new TransactionConnectorOutsideStation(station, connectorId);
        }

        if (string.IsNullOrWhiteSpace(idTag))
        {
            return new TransactionIdTagInvalid();
        }

        var open = await sessionQueries.FindOpenAsync(station.Id, connectorId, cancellationToken);
        if (open is not null)
        {
            return new TransactionConcurrent(open);
        }

        var now = clock.GetUtcNow();
        ChargingSession session;
        try
        {
            session = ChargingSession.Start(station.TenantId, station.Id, connectorId, now, meterStartKwh);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return new TransactionStartRejected(exception.Message);
        }

        station.MarkSeen(now);
        await sessionCommands.AddAsync(session, cancellationToken);
        return new TransactionStarted(session);
    }
}

/// <summary>What a StartTransaction ended in; the caller maps each to its own answer.</summary>
public abstract record TransactionStartOutcome;

/// <summary>The session is stored and the station is marked seen.</summary>
public sealed record TransactionStarted(ChargingSession Session) : TransactionStartOutcome;

/// <summary>The id tag is missing; OCPP answers the device's own Invalid decision under transaction 0.</summary>
public sealed record TransactionIdTagInvalid : TransactionStartOutcome;

/// <summary>The connector already has a running transaction, which the answer points at.</summary>
public sealed record TransactionConcurrent(ChargingSession Session) : TransactionStartOutcome;

/// <summary>The station exists but has no such connector; the caller explains the station's size.</summary>
public sealed record TransactionConnectorOutsideStation(Station Station, int ConnectorId) : TransactionStartOutcome;

/// <summary>The domain refused the session's start reading; the message is the domain's own.</summary>
public sealed record TransactionStartRejected(string Message) : TransactionStartOutcome;
