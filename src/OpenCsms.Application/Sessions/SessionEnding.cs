namespace OpenCsms.Application.Sessions;

using OpenCsms.Application.Ports;
using OpenCsms.Contracts;
using OpenCsms.Domain;

/// <summary>
/// The one way a session ends: the REST endpoint and the OCPP gateway both call this, so a session
/// stopped by the operator's dashboard and one stopped by the charger end the same way and produce the
/// same <c>session.ended</c> event. The end and its event are committed together, and the publish is
/// only attempted afterwards: when the broker is unreachable the session stays ended, the event stays
/// in the store, and the outbox's dispatcher retries it. The domain failures still surface here for
/// the caller to map to its own error shape.
/// </summary>
public sealed class SessionEnding(ISessionCommands sessions, IOutbox outbox)
{
    /// <summary>Ends an open session, saves it with its event, and attempts the publish.</summary>
    public async Task EndAsync(
        ChargingSession session,
        DateTimeOffset endedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        // The aggregate's stop is idempotent for a device that resends it; the REST caller treats a
        // duplicate end as the caller error it is.
        if (!session.Stop(endedAtUtc))
        {
            throw new InvalidOperationException("The session has already ended.");
        }

        outbox.Enqueue(
            CsmsEvents.SessionEndedRoutingKey,
            new SessionEnded(
                session.Id,
                session.TenantId,
                session.StationId,
                session.ConnectorId,
                session.StartedAtUtc,
                session.EndedAtUtc!.Value,
                session.EnergyKwh));
        await sessions.SaveAsync(cancellationToken);
        await outbox.DispatchEnqueuedAsync(cancellationToken);
    }
}
