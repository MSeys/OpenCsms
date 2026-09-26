namespace OpenCsms.Application.Ports;

using OpenCsms.Domain;

/// <summary>The invoice reads a use case needs; implemented over the CSMS store.</summary>
public interface IInvoiceQueries
{
    /// <summary>Finds an invoice by id, or null when none is stored.</summary>
    Task<Invoice?> FindAsync(Guid invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Finds an invoice by id within one tenant, or null when it is not that tenant's.</summary>
    Task<Invoice?> FindForTenantAsync(Guid invoiceId, string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Finds the invoice a session produced, or null while the billing worker has not billed it.</summary>
    Task<Invoice?> FindBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>The tenant's invoices, newest first.</summary>
    Task<IReadOnlyList<Invoice>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default);
}
