namespace OpenCsms.Application.Ports;

/// <summary>
/// The durable hand-off between a state change and its integration event: a use case stores the event
/// in the same unit of work as the state it belongs to, and the outbox publishes it afterwards. A
/// publish that fails schedules the row's next attempt instead of throwing, so the caller never sees
/// a store that disagrees with the broker, and the event stays in the store until an attempt succeeds.
/// </summary>
public interface IOutbox
{
    /// <summary>
    /// Adds the event to the current unit of work. The caller's save commits it together with the
    /// state change, and the rows this instance enqueued are what
    /// <see cref="DispatchEnqueuedAsync"/> attempts to publish.
    /// </summary>
    void Enqueue<T>(string routingKey, T message);

    /// <summary>
    /// Attempts to publish the rows this unit of work enqueued, as soon as the calling flow is ready
    /// to pay for the attempt. A failure leaves the row pending for the dispatcher's retry and never
    /// surfaces to the caller.
    /// </summary>
    Task DispatchEnqueuedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the pending rows that are due, oldest first. The background dispatcher's sweep, so an
    /// event whose immediate attempt failed reaches the broker once it is reachable again.
    /// </summary>
    Task DispatchDueAsync(CancellationToken cancellationToken = default);
}
