namespace OpenCsms.Infrastructure.Notifications;

/// <summary>
/// The notification targets' configuration. Each notification kind has its own base address, because
/// invoice-ready traffic goes to a PSP or email relay while billing failures go to the operator's
/// alerting webhook; the paths are the product's contract with those targets. The worker stays idle
/// for a kind whose address is not configured.
/// </summary>
public static class NotificationSettings
{
    public const string InvoiceReadyBaseUrlKey = "Notifications:InvoiceReadyBaseUrl";

    public const string BillingFailureBaseUrlKey = "Notifications:BillingFailureBaseUrl";

    /// <summary>The path under the invoice-ready base address; the invoice id follows it.</summary>
    public const string InvoiceReadyPath = "/notifications/invoice-ready";

    /// <summary>The path under the failure base address; the session id follows it.</summary>
    public const string BillingFailurePath = "/notifications/billing-failed";
}
