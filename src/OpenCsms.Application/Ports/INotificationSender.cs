namespace OpenCsms.Application.Ports;

/// <summary>
/// The application's outbound notification port: the notification worker hands it the events that
/// deserve an external call, and the infrastructure decides how the target is reached. The
/// application never sees an HTTP client or a target address; each notification kind carries its own
/// facts, so the adapter is a thin transport.
/// </summary>
public interface INotificationSender
{
    /// <summary>Pushes an issued invoice to the invoice-ready target (a PSP or an email relay).</summary>
    ValueTask SendInvoiceReadyAsync(
        InvoiceReadyNotification notification,
        CancellationToken cancellationToken = default);

    /// <summary>Pushes a billing failure to the operator's alerting target (a webhook).</summary>
    ValueTask SendBillingFailureAsync(
        BillingFailureNotification notification,
        CancellationToken cancellationToken = default);
}

/// <summary>The invoice-ready notification's payload: the invoice facts the target needs.</summary>
public sealed record InvoiceReadyNotification(
    Guid InvoiceId,
    Guid SessionId,
    string TenantId,
    decimal Total,
    string Currency,
    DateTimeOffset IssuedAtUtc);

/// <summary>The billing-failure notification's payload: which session failed and why.</summary>
public sealed record BillingFailureNotification(
    Guid SessionId,
    string TenantId,
    string Reason,
    DateTimeOffset FailedAtUtc);
