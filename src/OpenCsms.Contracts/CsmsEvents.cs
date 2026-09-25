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

/// <summary>The broker topology the API and the worker share.</summary>
public static class CsmsEvents
{
    /// <summary>The topic exchange both sides declare; durable, so an API restart is not a message loss.</summary>
    public const string Exchange = "csms.events";

    public const string SessionEndedRoutingKey = "session.ended";

    public const string InvoiceIssuedRoutingKey = "invoice.issued";

    /// <summary>The billing worker's queue and its dead-letter queue.</summary>
    public const string BillingQueue = "billing.session-ended";

    public const string BillingDeadLetterQueue = "billing.session-ended.dlq";

    /// <summary>Where the billing queue dead-letters a message it cannot process after its retries.</summary>
    public const string DeadLetterExchange = "csms.events.dlx";
}
