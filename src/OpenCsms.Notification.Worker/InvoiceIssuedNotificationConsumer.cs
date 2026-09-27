namespace OpenCsms.Notification.Worker;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenCsms.Application.Ports;
using OpenCsms.Contracts;
using OpenCsms.Infrastructure.Messaging;
using OpenCsms.Infrastructure.Notifications;

/// <summary>
/// Consumes <c>invoice.issued</c> and pushes each invoice to the external invoice-ready target - the
/// PSP or the email relay that tells the customer to pay. Delivery failures ride the shared consumer
/// loop: three in-process retries, then the notification is dead-lettered. Without a configured
/// invoice-ready base address the consumer stays idle instead of retrying against a target nobody
/// named.
/// </summary>
public sealed class InvoiceIssuedNotificationConsumer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<InvoiceIssuedNotificationConsumer> logger)
    : RetryingQueueConsumer<InvoiceIssued>(scopeFactory, configuration, logger)
{
    protected override string QueueName => CsmsEvents.NotificationInvoiceIssuedQueue;

    protected override string? DisabledReason =>
        string.IsNullOrWhiteSpace(Configuration[NotificationSettings.InvoiceReadyBaseUrlKey])
            ? $"No invoice-ready target is configured ('{NotificationSettings.InvoiceReadyBaseUrlKey}')."
            : null;

    protected override async ValueTask HandleAsync(InvoiceIssued message, CancellationToken cancellationToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<INotificationSender>();
        await sender.SendInvoiceReadyAsync(
            new InvoiceReadyNotification(
                message.InvoiceId,
                message.SessionId,
                message.TenantId,
                message.Total,
                message.Currency,
                message.IssuedAtUtc),
            cancellationToken);
    }
}
