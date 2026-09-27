namespace OpenCsms.Infrastructure.Notifications;

using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenCsms.Application.Ports;

/// <summary>
/// The <see cref="INotificationSender"/> over HTTP: each notification becomes a JSON POST to its
/// kind's configured base address, with the entity id in the path so the target (and a test's fake)
/// can name what arrived. A non-success status is a delivery failure and surfaces to the consumer, so
/// its retry path sees it; the sender never swallows a rejection. The addresses are read when a
/// notification is sent, so a host picks up the run's settings even though it was registered before
/// they arrived.
/// </summary>
public sealed class HttpNotificationSender(
    IConfiguration configuration,
    HttpClient httpClient,
    ILogger<HttpNotificationSender> logger) : INotificationSender
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask SendInvoiceReadyAsync(
        InvoiceReadyNotification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var url = $"{BaseUrl(NotificationSettings.InvoiceReadyBaseUrlKey)}" +
                  $"{NotificationSettings.InvoiceReadyPath}/{notification.InvoiceId}";
        await PostAsync(
            url,
            new
            {
                @event = "invoice-ready",
                notification.InvoiceId,
                notification.SessionId,
                notification.TenantId,
                notification.Total,
                notification.Currency,
                notification.IssuedAtUtc
            },
            notification.InvoiceId,
            cancellationToken);
    }

    public async ValueTask SendBillingFailureAsync(
        BillingFailureNotification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var url = $"{BaseUrl(NotificationSettings.BillingFailureBaseUrlKey)}" +
                  $"{NotificationSettings.BillingFailurePath}/{notification.SessionId}";
        await PostAsync(
            url,
            new
            {
                @event = "billing-failed",
                notification.SessionId,
                notification.TenantId,
                notification.Reason,
                notification.FailedAtUtc
            },
            notification.SessionId,
            cancellationToken);
    }

    private string BaseUrl(string key)
    {
        var configured = configuration[key];
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"No notification target is configured. Set '{key}' to the target's base address.");
        }

        return configured.TrimEnd('/');
    }

    private async ValueTask PostAsync(
        string url,
        object payload,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await httpClient.PostAsync(url, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"The notification target answered {(int)response.StatusCode} " +
                $"({response.ReasonPhrase}) for '{entityId}' at '{url}'.");
        }

        logger.LogInformation(
            "Notification for '{EntityId}' accepted by '{Url}' ({Status}).",
            entityId,
            url,
            (int)response.StatusCode);
    }
}
