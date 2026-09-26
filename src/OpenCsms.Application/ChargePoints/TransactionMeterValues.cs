namespace OpenCsms.Application.ChargePoints;

using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>
/// Records a MeterValues batch's reading on the transaction it names. The domain's rules stay the
/// last word: a reading below the start reading or below the last one throws, and the caller answers
/// with its own error shape; a reading for an ended session never reaches the aggregate.
/// </summary>
public sealed class TransactionMeterValues(
    ISessionQueries sessionQueries,
    ISessionCommands sessionCommands,
    TimeProvider clock)
{
    /// <summary>Records the reading, or reports the state that refused it.</summary>
    public async Task<MeterValuesOutcome> RecordAsync(
        Station station,
        int transactionId,
        decimal totalKwh,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(station);
        var session = await sessionQueries.FindByTransactionAsync(station.Id, transactionId, cancellationToken);
        if (session is null)
        {
            return new MeterValuesTransactionMissing(station, transactionId);
        }

        if (!session.IsOpen)
        {
            return new MeterValuesTransactionEnded(session);
        }

        try
        {
            session.AddMeterValue(clock.GetUtcNow(), totalKwh);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return new MeterValuesRejected(exception.Message);
        }

        await sessionCommands.SaveAsync(cancellationToken);
        return new MeterValuesRecorded(session);
    }
}

/// <summary>What a MeterValues call ended in; the caller maps each to its own answer.</summary>
public abstract record MeterValuesOutcome;

/// <summary>The reading is recorded on the session.</summary>
public sealed record MeterValuesRecorded(ChargingSession Session) : MeterValuesOutcome;

/// <summary>No session on the station carries the transaction number the charge point named.</summary>
public sealed record MeterValuesTransactionMissing(Station Station, int TransactionId) : MeterValuesOutcome;

/// <summary>The transaction exists but its session has already ended.</summary>
public sealed record MeterValuesTransactionEnded(ChargingSession Session) : MeterValuesOutcome;

/// <summary>The domain refused the reading; the message is the domain's own.</summary>
public sealed record MeterValuesRejected(string Message) : MeterValuesOutcome;
