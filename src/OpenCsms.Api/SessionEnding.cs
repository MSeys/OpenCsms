namespace OpenCsms.Api;

using Microsoft.EntityFrameworkCore;
using OpenCsms.Contracts;
using OpenCsms.Data;
using OpenCsms.Domain;
using OpenCsms.Messaging;

/// <summary>
/// The one way a session ends: the REST endpoint and the OCPP gateway both call these two steps in
/// this order, so a session stopped by the operator's dashboard and one stopped by the charger end the
/// same way and publish the same <c>session.ended</c> event. The domain failures surface at
/// <see cref="EndAsync"/> for the caller to map to its own error shape.
/// </summary>
internal static class SessionEnding
{
    /// <summary>Ends an open session and saves it; the domain's rejection rules still apply.</summary>
    public static async Task EndAsync(
        CsmsDbContext db,
        ChargingSession session,
        DateTimeOffset endedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(session);

        session.End(endedAtUtc);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Publishes <c>session.ended</c> for a session this flow just ended.</summary>
    public static ValueTask PublishAsync(
        IEventPublisher publisher,
        ChargingSession session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(publisher);
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
