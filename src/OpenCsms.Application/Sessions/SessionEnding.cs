namespace OpenCsms.Application.Sessions;

using OpenCsms.Application.Ports;
using OpenCsms.Contracts;
using OpenCsms.Domain;

/// <summary>
/// The one way a session ends: the REST endpoint and the OCPP gateway both call these two steps in
/// this order, so a session stopped by the operator's dashboard and one stopped by the charger end the
/// same way and publish the same <c>session.ended</c> event. The domain failures surface at
/// <see cref="EndAsync"/> for the caller to map to its own error shape; a publish failure surfaces at
/// <see cref="PublishAsync"/>, after the end is already stored, exactly as it always has.
/// </summary>
public sealed class SessionEnding(ISessionCommands sessions, IEventPublisher publisher)
{
    /// <summary>Ends an open session and saves it; the domain's rejection rules still apply.</summary>
    public async Task EndAsync(
        ChargingSession session,
        DateTimeOffset endedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        // The aggregate's stop is idempotent for a device that resends it; the REST caller treats a
        // duplicate end as the caller error it is, exactly as before.
        if (!session.Stop(endedAtUtc))
        {
            throw new InvalidOperationException("The session has already ended.");
        }

        await sessions.SaveAsync(cancellationToken);
    }

    /// <summary>Publishes <c>session.ended</c> for a session this flow just ended.</summary>
    public ValueTask PublishAsync(ChargingSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return publisher.PublishAsync(
            CsmsEvents.SessionEndedRoutingKey,
            new SessionEnded(
                session.Id,
                session.TenantId,
                session.StationId,
                session.ConnectorId,
                session.StartedAtUtc,
                session.EndedAtUtc!.Value,
                session.EnergyKwh),
            cancellationToken);
    }
}
