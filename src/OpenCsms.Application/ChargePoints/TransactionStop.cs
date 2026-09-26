namespace OpenCsms.Application.ChargePoints;

using OpenCsms.Application.Ports;
using OpenCsms.Application.Sessions;
using OpenCsms.Domain;

/// <summary>
/// Ends the transaction a StopTransaction names: a stop reading above the session's last one is
/// recorded first, the session ends through the same <see cref="SessionEnding"/> the REST endpoint
/// uses, and <c>session.ended</c> is published once. A stop for a session that already ended is a
/// no-op the caller answers without processing it twice - a charge point that lost the answer may
/// resend it.
/// </summary>
public sealed class TransactionStop(
    ISessionQueries sessionQueries,
    SessionEnding ending,
    TimeProvider clock)
{
    /// <summary>Ends the transaction, or reports the state that refused it.</summary>
    public async Task<TransactionStopOutcome> StopAsync(
        Station station,
        int transactionId,
        decimal meterStopKwh,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(station);
        var session = await sessionQueries.FindByTransactionAsync(station.Id, transactionId, cancellationToken);
        if (session is null)
        {
            return new TransactionStopMissing(station, transactionId);
        }

        if (!session.IsOpen)
        {
            return new TransactionStopAlreadyEnded(session);
        }

        var now = clock.GetUtcNow();

        // Only a stop reading above the last recorded register adds energy; a stop below it keeps the
        // session's last reading, exactly like a late MeterValues would.
        if (meterStopKwh > session.LastReadingKwh)
        {
            try
            {
                session.AddMeterValue(now, meterStopKwh);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return new StopReadingRejected(exception.Message);
            }
        }

        try
        {
            await ending.EndAsync(session, now, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return new StopReadingRejected(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return new StopOperationRejected(exception.Message);
        }

        await ending.PublishAsync(session, cancellationToken);
        return new TransactionStopped(session);
    }
}

/// <summary>What a StopTransaction ended in; the caller maps each to its own answer.</summary>
public abstract record TransactionStopOutcome;

/// <summary>The session is ended, saved and its <c>session.ended</c> event published.</summary>
public sealed record TransactionStopped(ChargingSession Session) : TransactionStopOutcome;

/// <summary>No session on the station carries the transaction number the charge point named.</summary>
public sealed record TransactionStopMissing(Station Station, int TransactionId) : TransactionStopOutcome;

/// <summary>The transaction already ended; the resend changed nothing.</summary>
public sealed record TransactionStopAlreadyEnded(ChargingSession Session) : TransactionStopOutcome;

/// <summary>The domain refused the stop reading; the message is the domain's own.</summary>
public sealed record StopReadingRejected(string Message) : TransactionStopOutcome;

/// <summary>The stop reached the session in a state the domain refuses with an invalid operation.</summary>
public sealed record StopOperationRejected(string Message) : TransactionStopOutcome;
