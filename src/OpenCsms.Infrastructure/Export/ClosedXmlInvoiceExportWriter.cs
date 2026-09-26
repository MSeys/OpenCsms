namespace OpenCsms.Infrastructure.Export;

using ClosedXML.Excel;
using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>
/// The monthly export's spreadsheet mechanics, over ClosedXML: one <c>Invoices</c> sheet carrying
/// the month's rows as a plain table (header plus one row per stored invoice), and one
/// <c>Summary</c> sheet carrying the month, the row count and the summed total. Totals are written
/// as literal values the use case summed, not as formulas, so the file carries exactly what the
/// invoice rows hold and no spreadsheet engine needs to recalculate it.
/// </summary>
internal sealed class ClosedXmlInvoiceExportWriter : IInvoiceExportWriter
{
    public byte[] WriteMonthlyExport(IReadOnlyList<Invoice> invoices, string month)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentException.ThrowIfNullOrWhiteSpace(month);
        using var workbook = new XLWorkbook();
        WriteInvoices(workbook.AddWorksheet("Invoices"), invoices);
        WriteSummary(workbook.AddWorksheet("Summary"), invoices, month);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteInvoices(IXLWorksheet sheet, IReadOnlyList<Invoice> invoices)
    {
        var headers = new[]
        {
            "InvoiceId", "SessionId", "IssuedAtUtc", "EnergyKwh", "EnergyAmount",
            "StartFeeAmount", "IdleHours", "IdleFeeAmount", "Total", "Currency"
        };
        for (var column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
        }

        for (var row = 0; row < invoices.Count; row++)
        {
            var invoice = invoices[row];
            var line = row + 2;
            sheet.Cell(line, 1).Value = invoice.Id.ToString();
            sheet.Cell(line, 2).Value = invoice.SessionId.ToString();
            sheet.Cell(line, 3).Value = invoice.IssuedAtUtc.UtcDateTime;
            sheet.Cell(line, 3).Style.DateFormat.Format = "yyyy-MM-ddTHH:mm:ssZ";
            sheet.Cell(line, 4).Value = invoice.EnergyKwh;
            sheet.Cell(line, 5).Value = invoice.EnergyAmount;
            sheet.Cell(line, 6).Value = invoice.StartFeeAmount;
            sheet.Cell(line, 7).Value = invoice.IdleHours;
            sheet.Cell(line, 8).Value = invoice.IdleFeeAmount;
            sheet.Cell(line, 9).Value = invoice.Total;
            sheet.Cell(line, 10).Value = invoice.Currency;
        }
    }

    private static void WriteSummary(IXLWorksheet sheet, IReadOnlyList<Invoice> invoices, string month)
    {
        sheet.Cell("A1").Value = "Month";
        sheet.Cell("B1").Value = month;
        sheet.Cell("A2").Value = "Invoices";
        sheet.Cell("B2").Value = invoices.Count;
        sheet.Cell("A3").Value = "Total";
        sheet.Cell("B3").Value = invoices.Sum(invoice => invoice.Total);
        sheet.Cell("A4").Value = "Currency";
        sheet.Cell("B4").Value = invoices.Count == 0 ? "EUR" : invoices[0].Currency;
    }
}
