namespace OpenCsms.Notification.Worker;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenCsms.Application.Ports;
using OpenCsms.Contracts;
using OpenCsms.Infrastructure.Messaging;
using OpenCsms.Infrastructure.Notifications;

/// <summary>
/// Consumes <c>billing.failed</c> - the billing worker's report that a session could not be billed -
/// and pushes it to the operator's alerting target, so a customer who will never get an invoice is
/// visible. Delivery failures ride the shared consumer loop: three in-process retries, then the
/// notification is dead-lettered. Without a configured failure base address the consumer stays idle.
/// </summary>
public sealed class BillingFailedNotificationConsumer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<BillingFailedNotificationConsumer> logger)
    : RetryingQueueConsumer<SessionBillingFailed>(scopeFactory, configuration, logger)
{
    protected override string QueueName => CsmsEvents.NotificationBillingFailedQueue;

    protected override string? DisabledReason =>
        string.IsNullOrWhiteSpace(Configuration[NotificationSettings.BillingFailureBaseUrlKey])
            ? $"No billing-failure target is configured ('{NotificationSettings.BillingFailureBaseUrlKey}')."
            : null;

    protected override async ValueTask HandleAsync(SessionBillingFailed message, CancellationToken cancellationToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<INotificationSender>();
        await sender.SendBillingFailureAsync(
            new BillingFailureNotification(
                message.SessionId,
                message.TenantId,
                message.Reason,
                message.FailedAtUtc),
            cancellationToken);
    }
}
