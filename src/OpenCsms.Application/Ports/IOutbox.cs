namespace OpenCsms.Application.Ports;

/// <summary>
/// The durable hand-off between a state change and its event: a use case stores the event in the
/// same unit of work as the state it belongs to, and the outbox publishes it afterwards. A publish
/// that fails schedules the row's next attempt instead of throwing, so the caller never sees a store
/// that disagrees with the broker.
/// </summary>
public interface IOutbox
{
    /// <summary>Adds the event to the current unit of work, to be committed with the state change.</summary>
    void Enqueue<T>(string routingKey, T message);

    /// <summary>Publishes the rows this unit of work enqueued, as soon as the calling flow can wait.</summary>
    Task DispatchEnqueuedAsync(CancellationToken cancellationToken = default);

    /// <summary>Publishes the pending rows that are due, oldest first.</summary>
    Task DispatchDueAsync(CancellationToken cancellationToken = default);
}
