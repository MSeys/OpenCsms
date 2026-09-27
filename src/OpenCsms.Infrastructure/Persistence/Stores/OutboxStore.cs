namespace OpenCsms.Infrastructure.Persistence.Stores;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCsms.Application.Ports;

/// <summary>
/// The outbox over the one CSMS store. <see cref="Enqueue{T}"/> writes the event into the caller's
/// unit of work, so the store never holds a state change without its event, and the dispatch methods
/// publish the stored rows with at-least-once semantics. An attempt reserves its row before
/// publishing: two dispatchers never publish the same attempt twice, and a row whose dispatcher died
/// mid-publish becomes available again when the reservation expires. A failed attempt records the
/// failure and schedules the next one with a bounded backoff instead of throwing, so a broker outage
/// never surfaces as a lost event or a failed request.
/// </summary>
public sealed class OutboxStore(
    CsmsDbContext db,
    IEventPublisher publisher,
    TimeProvider clock,
    ILogger<OutboxStore> logger) : IOutbox
{
    /// <summary>How many due rows one sweep publishes.</summary>
    private const int BatchSize = 32;

    /// <summary>The first retry after a failed publish; every further failure doubles it.</summary>
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>Retries never wait longer than this, so a recovered broker is used again soon.</summary>
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>How long an attempt owns its row before another dispatcher may take it over.</summary>
    private static readonly TimeSpan AttemptReservation = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly List<Guid> _enqueued = [];

    /// <inheritdoc />
    public void Enqueue<T>(string routingKey, T message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);
        ArgumentNullException.ThrowIfNull(message);
        var row = new OutboxMessage(
            routingKey,
            JsonSerializer.Serialize(message, JsonOptions),
            clock.GetUtcNow(),
            // The flow that commits the event gets the first publish attempt: the reservation keeps
            // every other dispatcher away until the attempt had its chance.
            DateTimeOffset.UtcNow + AttemptReservation);
        db.OutboxMessages.Add(row);
        _enqueued.Add(row.Id);
    }

    /// <inheritdoc />
    public async Task DispatchEnqueuedAsync(CancellationToken cancellationToken = default)
    {
        var enqueued = _enqueued.ToArray();
        _enqueued.Clear();
        foreach (var id in enqueued)
        {
            try
            {
                await TryPublishAsync(id, reservedForCaller: true, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller stopped waiting; the row keeps its reservation and the dispatcher
                // retries it when the reservation expires.
                return;
            }
            catch (Exception exception)
            {
                // The event is committed either way; the row stays pending for the next sweep.
                logger.LogWarning(
                    exception,
                    "The outbox's immediate publish attempt failed; the event stays pending.");
            }
        }
    }

    /// <inheritdoc />
    public async Task DispatchDueAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var due = await db.OutboxMessages
            .AsNoTracking()
            .Where(row => row.SentAtUtc == null && row.NextAttemptAtUtc <= now)
            .OrderBy(row => row.OccurredAtUtc)
            .ThenBy(row => row.Id)
            .Select(row => row.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        foreach (var id in due)
        {
            await TryPublishAsync(id, reservedForCaller: false, cancellationToken);
        }
    }

    /// <summary>
    /// Claims one row for one attempt and publishes it. A row that was already sent, or that another
    /// dispatcher reserved, is left alone; a row whose attempt failed gets its next attempt scheduled.
    /// </summary>
    private async Task TryPublishAsync(Guid id, bool reservedForCaller, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var claim = db.OutboxMessages.Where(row => row.Id == id && row.SentAtUtc == null);
        claim = reservedForCaller
            // The enqueue reserved the row for the flow that committed it; another dispatcher's
            // claim moves the same instant forward, so whichever claim lands first owns the attempt.
            ? claim.Where(row => row.NextAttemptAtUtc > now)
            : claim.Where(row => row.NextAttemptAtUtc <= now);
        var claimed = await claim.ExecuteUpdateAsync(
            update => update.SetProperty(row => row.NextAttemptAtUtc, now + AttemptReservation),
            cancellationToken);
        if (claimed == 0)
        {
            return;
        }

        var row = await db.OutboxMessages
            .AsNoTracking()
            .Where(candidate => candidate.Id == id)
            .Select(candidate => new { candidate.RoutingKey, candidate.PayloadJson, candidate.Attempts })
            .SingleAsync(cancellationToken);
        try
        {
            using var payload = JsonDocument.Parse(row.PayloadJson);
            await publisher.PublishAsync(row.RoutingKey, payload.RootElement, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Publishing outbox event '{RoutingKey}' failed; the next attempt is scheduled.",
                row.RoutingKey);
            await ScheduleRetryAsync(id, row.Attempts);
            return;
        }

        await db.OutboxMessages
            .Where(candidate => candidate.Id == id)
            .ExecuteUpdateAsync(
                update => update.SetProperty(candidate => candidate.SentAtUtc, DateTimeOffset.UtcNow),
                cancellationToken);
    }

    private async Task ScheduleRetryAsync(Guid id, int failedAttempts)
    {
        var nextAttemptAt = DateTimeOffset.UtcNow + RetryDelay(failedAttempts + 1);
        // The caller's token may already be cancelled - the attempt failed anyway - so the retry is
        // recorded with no token: the row must not stay reserved until the reservation expires.
        await db.OutboxMessages
            .Where(row => row.Id == id)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(row => row.Attempts, row => row.Attempts + 1)
                    .SetProperty(row => row.NextAttemptAtUtc, nextAttemptAt),
                CancellationToken.None);
    }

    /// <summary>The bounded backoff: a second doubling per failed attempt, capped at half a minute.</summary>
    private static TimeSpan RetryDelay(int failedAttempts)
        => TimeSpan.FromSeconds(Math.Min(
            FirstRetryDelay.TotalSeconds * Math.Pow(2, failedAttempts - 1),
            MaxRetryDelay.TotalSeconds));
}
