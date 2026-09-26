namespace OpenCsms.Application.Billing;

using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>
/// The monthly invoice export: the tenant's invoices issued inside the named month, oldest first,
/// composed into the workbook the export port produces. The tenant comes from the caller's session,
/// never from the request, so one operator's file never carries another tenant's rows; the numbers
/// are the stored invoice rows themselves, so the file always matches the invoice view.
/// </summary>
public sealed class MonthlyInvoiceExport(IInvoiceQueries invoices, IInvoiceExportWriter writer)
{
    /// <summary>Exports the tenant's month; the month text must be <c>YYYY-MM</c>.</summary>
    public async Task<MonthlyExport> ExportAsync(
        string tenantId,
        string? monthText,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var month = InvoiceMonth.Parse(monthText);
        var rows = (await invoices.ListByTenantAsync(tenantId, cancellationToken))
            .Where(invoice => month.Contains(invoice.IssuedAtUtc))
            .OrderBy(invoice => invoice.IssuedAtUtc)
            .ThenBy(invoice => invoice.Id)
            .ToArray();
        var content = writer.WriteMonthlyExport(rows, month.ToString());
        return new MonthlyExport(month.ToString(), rows, content);
    }
}

/// <summary>The exported month: the rows it holds and the workbook bytes that carry them.</summary>
public sealed record MonthlyExport(string Month, IReadOnlyList<Invoice> Rows, byte[] Content)
{
    /// <summary>The download's file name, so the browser saves one file per month.</summary>
    public string FileName => $"invoices-{Month}.xlsx";
}
