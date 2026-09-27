namespace OpenCsms.Infrastructure.Persistence;

/// <summary>
/// One integration event waiting for the broker: the row is written in the same transaction as the
/// state that produced it and leaves the pending set only when a publish attempt succeeded, so an
/// unreachable broker delays the event instead of losing it.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
        RoutingKey = string.Empty;
        PayloadJson = string.Empty;
    }

    public OutboxMessage(
        string routingKey,
        string payloadJson,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset reservedUntilUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);
        ArgumentNullException.ThrowIfNull(payloadJson);
        Id = Guid.NewGuid();
        RoutingKey = routingKey;
        PayloadJson = payloadJson;
        OccurredAtUtc = occurredAtUtc;
        NextAttemptAtUtc = reservedUntilUtc;
    }

    public Guid Id { get; private set; }

    /// <summary>The exchange routing key the payload travels under, as the publisher sends it.</summary>
    public string RoutingKey { get; private set; }

    /// <summary>The serialized event, stored as the publisher will send it.</summary>
    public string PayloadJson { get; private set; }

    /// <summary>When the state change that produced the event happened, on the application clock.</summary>
    public DateTimeOffset OccurredAtUtc { get; private set; }

    /// <summary>When a publish attempt succeeded; null while the event is still pending.</summary>
    public DateTimeOffset? SentAtUtc { get; private set; }

    /// <summary>How many publish attempts failed before the row was sent, for diagnosis.</summary>
    public int Attempts { get; private set; }

    /// <summary>
    /// When a dispatcher may pick the row up, on the machine clock: retry pacing is infrastructure
    /// time, like the billing consumer's own retry delay, while <see cref="OccurredAtUtc"/> carries
    /// the application clock. An attempt moves this instant forward to reserve the row, so parallel
    /// dispatchers do not publish the same attempt twice and a dispatcher that dies mid-publish
    /// releases its row when the reservation expires.
    /// </summary>
    public DateTimeOffset NextAttemptAtUtc { get; private set; }
}
