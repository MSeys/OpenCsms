namespace OpenCsms.Application.Sessions;

using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>
/// Records one meter reading on an open session. The domain's rules stay the last word: a reading
/// that moves backwards in time or energy throws, and the caller answers with its own error shape.
/// </summary>
public sealed class SessionMeterValues(ISessionQueries sessions, ISessionCommands commands, TimeProvider clock)
{
    /// <summary>Records the reading and saves; null when no session carries the id.</summary>
    public async Task<ChargingSession?> RecordAsync(
        Guid sessionId,
        decimal totalKwh,
        CancellationToken cancellationToken = default)
    {
        var session = await sessions.FindAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        session.AddMeterValue(clock.GetUtcNow(), totalKwh);
        await commands.SaveAsync(cancellationToken);
        return session;
    }
}
