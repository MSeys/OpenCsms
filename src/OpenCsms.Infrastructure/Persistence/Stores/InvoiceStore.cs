namespace OpenCsms.Infrastructure.Persistence.Stores;

using Microsoft.EntityFrameworkCore;
using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>The invoice queries and commands, over the one CSMS store.</summary>
public sealed class InvoiceStore(CsmsDbContext db) : IInvoiceQueries, IInvoiceCommands
{
    /// <inheritdoc />
    public Task<Invoice?> FindAsync(Guid invoiceId, CancellationToken cancellationToken = default)
        => db.Invoices.FirstOrDefaultAsync(invoice => invoice.Id == invoiceId, cancellationToken);

    /// <inheritdoc />
    public Task<Invoice?> FindForTenantAsync(Guid invoiceId, string tenantId, CancellationToken cancellationToken = default)
        => db.Invoices.FirstOrDefaultAsync(
            invoice => invoice.Id == invoiceId && invoice.TenantId == tenantId,
            cancellationToken);

    /// <inheritdoc />
    public Task<Invoice?> FindBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => db.Invoices.FirstOrDefaultAsync(invoice => invoice.SessionId == sessionId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Invoice>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default)
        => await db.Invoices
            .Where(invoice => invoice.TenantId == tenantId)
            .OrderByDescending(invoice => invoice.IssuedAtUtc)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(Invoice invoice, CancellationToken cancellationToken = default)
    {
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);
    }
}
