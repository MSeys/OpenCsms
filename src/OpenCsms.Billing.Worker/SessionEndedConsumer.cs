namespace OpenCsms.Billing.Worker;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenCsms.Application.Billing;
using OpenCsms.Application.Ports;
using OpenCsms.Contracts;
using OpenCsms.Infrastructure.Messaging;

/// <summary>
/// Consumes <c>session.ended</c> and hands each delivery to the application's billing use case,
/// <see cref="InvoiceIssuance"/>. The retry and dead-letter semantics are the shared consumer loop's;
/// what billing means, and which failures are retryable, is the use case's. A session the worker
/// gives up on is reported as <c>billing.failed</c> before the delivery is dead-lettered, so the
/// notification worker can tell the operator that an invoice will never appear. The unique session
/// index in the store makes storing idempotent.
/// </summary>
public sealed class SessionEndedConsumer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<SessionEndedConsumer> logger)
    : RetryingQueueConsumer<SessionEnded>(scopeFactory, configuration, logger)
{
    protected override string QueueName => CsmsEvents.BillingQueue;

    protected override async ValueTask HandleAsync(SessionEnded message, CancellationToken cancellationToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var issuance = scope.ServiceProvider.GetRequiredService<InvoiceIssuance>();
        await issuance.IssueAsync(message, cancellationToken);
    }

    protected override async ValueTask OnTerminalFailureAsync(
        SessionEnded? message,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (message is null)
        {
            return;
        }

        await using var scope = ScopeFactory.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        await publisher.PublishAsync(
            CsmsEvents.BillingFailedRoutingKey,
            new SessionBillingFailed(
                message.SessionId,
                message.TenantId,
                exception.Message,
                clock.GetUtcNow()),
            cancellationToken);
    }
}
