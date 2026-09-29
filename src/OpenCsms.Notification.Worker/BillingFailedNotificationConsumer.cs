namespace OpenCsms.Notification.Worker;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenCsms.Application.Ports;
using OpenCsms.Contracts;
using OpenCsms.Infrastructure.Messaging;
using OpenCsms.Infrastructure.Notifications;

/// <summary>
/// Pushes <c>billing.failed</c> to the operator's alerting target. Without a configured failure base
/// address the consumer stays idle.
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
