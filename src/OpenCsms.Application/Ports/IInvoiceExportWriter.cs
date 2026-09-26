namespace OpenCsms.Application.Ports;

using OpenCsms.Domain;

/// <summary>
/// The spreadsheet mechanics behind the monthly export. The application owns the composition - which
/// rows, which month, which tenant - and this port owns only turning those rows into workbook bytes,
/// so the workbook library stays an infrastructure detail the application never sees.
/// </summary>
public interface IInvoiceExportWriter
{
    /// <summary>Renders the month's invoice rows as an <c>.xlsx</c> workbook.</summary>
    byte[] WriteMonthlyExport(IReadOnlyList<Invoice> invoices, string month);
}
