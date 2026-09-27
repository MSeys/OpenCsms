namespace OpenCsms.Suite.Support;

using ProtoTest.Sheets;

/// <summary>
/// The monthly export's sheets as typed models: the invoices table and the summary's label/value
/// block. Tests assert by property, so the export's headers and summary labels are declared here and
/// nowhere else.
/// </summary>
[Sheet("Invoices")]
public sealed record InvoiceExportRow(
    [property: Column("InvoiceId", Unique = true)] string InvoiceId,
    [property: Column("SessionId", Unique = true)] string SessionId,
    [property: Column("IssuedAtUtc")] DateTime IssuedAtUtc,
    [property: Column("EnergyKwh", Min = 0)] decimal EnergyKwh,
    [property: Column("EnergyAmount", Min = 0)] decimal EnergyAmount,
    [property: Column("StartFeeAmount", Min = 0)] decimal StartFeeAmount,
    [property: Column("IdleHours", Min = 0)] int IdleHours,
    [property: Column("IdleFeeAmount", Min = 0)] decimal IdleFeeAmount,
    [property: Column("Total", Min = 0)] decimal Total,
    [property: Column("Currency", OneOf = ["EUR"])] string Currency);

/// <summary>The summary sheet's labels, as written by the export's summary block.</summary>
[Sheet("Summary", Kind = ProtoSheetKind.KeyValue)]
public sealed record InvoiceExportSummary(
    [property: Column("Month")] string Month,
    [property: Column("Invoices")] int Count,
    [property: Column("Total")] decimal Total,
    [property: Column("Currency")] string Currency);
