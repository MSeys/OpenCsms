namespace OpenCsms.Application.Billing;

using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>The invoice read surface: one invoice by id or session, or a tenant's newest first.</summary>
public sealed class InvoiceReads(IInvoiceQueries invoices)
{
    /// <summary>Finds an invoice by id, or null when none is stored.</summary>
    public Task<Invoice?> FindAsync(Guid invoiceId, CancellationToken cancellationToken = default)
        => invoices.FindAsync(invoiceId, cancellationToken);

    /// <summary>Finds an invoice by id within one tenant; another tenant's invoice looks unknown.</summary>
    public Task<Invoice?> FindForTenantAsync(Guid invoiceId, string tenantId, CancellationToken cancellationToken = default)
        => invoices.FindForTenantAsync(invoiceId, tenantId, cancellationToken);

    /// <summary>Finds the invoice a session produced, or null while the billing worker has not billed it.</summary>
    public Task<Invoice?> FindBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => invoices.FindBySessionAsync(sessionId, cancellationToken);

    /// <summary>The tenant's invoices, newest first.</summary>
    public Task<IReadOnlyList<Invoice>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default)
        => invoices.ListByTenantAsync(tenantId, cancellationToken);
}
