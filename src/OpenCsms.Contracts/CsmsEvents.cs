namespace OpenCsms.Contracts;

/// <summary>
/// Published by the API when a session ends; the billing worker consumes it. Wire DTOs stay primitive
/// on purpose: the worker loads the session and its tariff itself, so a stale event cannot bill wrong.
/// </summary>
public sealed record SessionEnded(
    Guid SessionId,
    string TenantId,
    Guid StationId,
    int ConnectorId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    decimal EnergyKwh);

/// <summary>Published by the billing worker after it stored an invoice.</summary>
public sealed record InvoiceIssued(
    Guid InvoiceId,
    Guid SessionId,
    string TenantId,
    decimal Total,
    string Currency,
    DateTimeOffset IssuedAtUtc);

/// <summary>
/// Published by the billing worker when a <c>session.ended</c> exhausted its retries and was
/// dead-lettered: the session could not be billed, and the notification worker tells the operator's
/// alerting target. The reason is the last failure's message.
/// </summary>
public sealed record SessionBillingFailed(
    Guid SessionId,
    string TenantId,
    string Reason,
    DateTimeOffset FailedAtUtc);

/// <summary>The broker topology the API and the workers share.</summary>
public static class CsmsEvents
{
    /// <summary>The topic exchange both sides declare; durable, so an API restart is not a message loss.</summary>
    public const string Exchange = "csms.events";

    public const string SessionEndedRoutingKey = "session.ended";

    public const string InvoiceIssuedRoutingKey = "invoice.issued";

    public const string BillingFailedRoutingKey = "billing.failed";

    /// <summary>The billing worker's queue and its dead-letter queue.</summary>
    public const string BillingQueue = "billing.session-ended";

    public const string BillingDeadLetterQueue = "billing.session-ended.dlq";

    /// <summary>Where the billing queue dead-letters a message it cannot process after its retries.</summary>
    public const string DeadLetterExchange = "csms.events.dlx";

    /// <summary>The notification worker's queues, each bound to its event's routing key.</summary>
    public const string NotificationInvoiceIssuedQueue = "notifications.invoice-issued";

    public const string NotificationBillingFailedQueue = "notifications.billing-failed";

    /// <summary>Where a notification the worker could not deliver after its retries is dead-lettered.</summary>
    public const string NotificationsDeadLetterExchange = "notifications.dlx";

    public const string NotificationInvoiceIssuedDeadLetterQueue = "notifications.invoice-issued.dlq";

    public const string NotificationBillingFailedDeadLetterQueue = "notifications.billing-failed.dlq";
}
