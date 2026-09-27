namespace OpenCsms.Suite.Journeys;

using System.Net;
using OpenCsms.Contracts;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.Sheets;

/// <summary>
/// The monthly export journey: the tenant's <c>.xlsx</c> is downloaded through the product endpoint
/// and asserted with <see cref="ProtoTest.Sheets"/> - the real cells, not a re-serialized object.
/// The month scopes every row, the row count matches the seeded busy month, the header uses the
/// invoice API's own names, every exported row carries its stored row's numbers, and the totals
/// match the stored invoice rows the dashboard reads. The other tenant's rows never appear.
/// </summary>
[Application(CsmsTargets.Api)]
public sealed class MonthlyInvoiceExport
{
    [ProtoTest]
    [RequiresSeededMonth]
    public async Task TheMonthlyExportMatchesTheStoredInvoiceRows()
    {
        await DashboardSession.SignInAsync(SeededMonth.TenantA.LoginEmail, SeededMonth.TenantA.LoginPassword);

        using var download = await Proto.Context.Rest().GetAsync("/api/invoices/export?month=2030-05");
        download.Should.HaveHttpStatus(HttpStatusCode.OK);
        Assert.That(
            download.ContentHeaders.ContentType?.MediaType,
            Is.EqualTo("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));

        var workbook = Proto.Context.Sheets().Open(download);
        var invoices = workbook.Sheet("Invoices");
        Assert.That(invoices.RowCount, Is.EqualTo(SeededMonth.SessionsPerTenant + 1), "header plus one row per seeded invoice");

        invoices.Range("A1:J1").Should.Match([[
            "InvoiceId", "SessionId", "IssuedAtUtc", "EnergyKwh", "EnergyAmount",
            "StartFeeAmount", "IdleHours", "IdleFeeAmount", "Total", "Currency"]]);

        var model = workbook.Model<ExportRow>();
        model.Should.MatchModel();
        Assert.That(model.Rows, Has.Count.EqualTo(SeededMonth.SessionsPerTenant));
        Assert.That(
            model.Rows.Select(row => row.IssuedAtUtc),
            Has.All.Matches<DateTime>(issued => issued.Year == 2030 && issued.Month == 5),
            "every row belongs to the requested month");
        var sheetTotal = model.Rows.Sum(row => row.Total);

        var summary = workbook.Sheet("Summary");
        summary.Cell("B1").Should.Be(SeededMonth.MonthText);
        summary.Cell("B2").Should.Be((double)SeededMonth.SessionsPerTenant);
        summary.Cell("B3").Should.Be((double)sheetTotal);
        summary.Cell("B4").Should.Be("EUR");

        // The file matches the invoice rows themselves: the dashboard list for the same tenant and
        // month carries the same count and the same summed total.
        using var listed = await Proto.Context.Rest().GetAsync("/api/dashboard/invoices");
        var stored = listed.ReadAsJson<InvoiceResponse[]>()!
            .Where(row => row.IssuedAtUtc.Year == 2030 && row.IssuedAtUtc.Month == 5)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(stored, Has.Length.EqualTo(SeededMonth.SessionsPerTenant));
            Assert.That(stored.Sum(row => row.Total), Is.EqualTo(sheetTotal));
            Assert.That(
                stored.Select(row => row.Id.ToString()).OrderBy(id => id),
                Is.EqualTo(model.Rows.Select(row => row.InvoiceId).OrderBy(id => id)),
                "the sheet carries exactly the stored rows");
        });

        // Every exported row carries its stored row's own numbers, so a swapped or renamed column
        // fails here rather than hiding behind the summed total. The spreadsheet's date serial has
        // coarser precision than the store, so the instant compares within a second.
        foreach (var row in model.Rows)
        {
            var match = stored.Single(candidate => candidate.Id.ToString() == row.InvoiceId);
            Assert.Multiple(() =>
            {
                Assert.That(row.SessionId, Is.EqualTo(match.SessionId.ToString()));
                Assert.That(row.IssuedAtUtc, Is.EqualTo(match.IssuedAtUtc.UtcDateTime).Within(TimeSpan.FromSeconds(1)));
                Assert.That(row.EnergyKwh, Is.EqualTo(match.EnergyKwh));
                Assert.That(row.EnergyAmount, Is.EqualTo(match.EnergyAmount));
                Assert.That(row.StartFeeAmount, Is.EqualTo(match.StartFeeAmount));
                Assert.That(row.IdleHours, Is.EqualTo(match.IdleHours));
                Assert.That(row.IdleFeeAmount, Is.EqualTo(match.IdleFeeAmount));
                Assert.That(row.Total, Is.EqualTo(match.Total));
                Assert.That(row.Currency, Is.EqualTo(match.Currency));
            });
        }
    }

    [ProtoTest]
    [RequiresSeededMonth]
    public async Task TheExportIsScopedToTheMonth()
    {
        await DashboardSession.SignInAsync(SeededMonth.TenantA.LoginEmail, SeededMonth.TenantA.LoginPassword);

        using var download = await Proto.Context.Rest().GetAsync("/api/invoices/export?month=2030-06");
        download.Should.HaveHttpStatus(HttpStatusCode.OK);

        var workbook = Proto.Context.Sheets().Open(download);
        var invoices = workbook.Sheet("Invoices");
        Assert.That(invoices.RowCount, Is.EqualTo(1), "a month with no invoices still carries the header");
        var summary = workbook.Sheet("Summary");
        summary.Cell("B2").Should.Be(0.0);
        summary.Cell("B3").Should.Be(0.0);
    }

    [ProtoTest]
    [RequiresSeededMonth]
    public async Task TheExportRefusesABadMonth()
    {
        await DashboardSession.SignInAsync(SeededMonth.TenantA.LoginEmail, SeededMonth.TenantA.LoginPassword);

        foreach (var month in new[] { "june", "202305", "2030-13", "2030-5" })
        {
            using var response = await Proto.Context.Rest().GetAsync($"/api/invoices/export?month={month}");
            response.Should.HaveHttpStatus(HttpStatusCode.BadRequest);
        }

        using var missing = await Proto.Context.Rest().GetAsync("/api/invoices/export");
        missing.Should.HaveHttpStatus(HttpStatusCode.BadRequest);
    }

    [ProtoTest]
    public async Task TheExportRefusesAnonymousReads()
    {
        using var response = await Proto.Context.Rest().GetAsync("/api/invoices/export?month=2030-05");

        response.Should.HaveHttpStatus(HttpStatusCode.Unauthorized);
    }

    [ProtoTest]
    [RequiresSeededMonth]
    public async Task OneTenantCannotExportTheOthersMonth()
    {
        var ownIds = await ExportInvoiceIdsAsync(SeededMonth.TenantA);
        var otherIds = await ExportInvoiceIdsAsync(SeededMonth.TenantB);

        Assert.Multiple(() =>
        {
            Assert.That(ownIds, Has.Count.EqualTo(SeededMonth.SessionsPerTenant));
            Assert.That(otherIds, Has.Count.EqualTo(SeededMonth.SessionsPerTenant));
            Assert.That(ownIds, Does.Not.Contain(otherIds.First()), "no row leaks across tenants");
        });
    }

    private static async Task<HashSet<string>> ExportInvoiceIdsAsync(SeededTenant tenant)
    {
        await DashboardSession.SignInAsync(tenant.LoginEmail, tenant.LoginPassword);
        using var download = await Proto.Context.Rest().GetAsync("/api/invoices/export?month=2030-05");
        download.Should.HaveHttpStatus(HttpStatusCode.OK);
        var model = Proto.Context.Sheets().Open(download).Model<ExportRow>();
        return model.Rows.Select(row => row.InvoiceId).ToHashSet(StringComparer.Ordinal);
    }

    [Sheet("Invoices")]
    private sealed record ExportRow(
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
}
