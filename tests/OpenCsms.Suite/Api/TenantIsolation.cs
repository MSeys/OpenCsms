namespace OpenCsms.Suite.Api;

using System.Net;
using OpenCsms.Contracts;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

/// <summary>
/// The multi-tenancy negative test: one operator must not see another operator's stations, sessions
/// or invoices, through the dashboard reads or its commands. A foreign id answers 404 exactly like
/// an unknown one, and the lists carry only the signed-in tenant's rows. No broker or worker is
/// needed: the foreign invoice comes from the run's seeded month, and the foreign session is an
/// open one the test starts over REST.
/// </summary>
[Application(CsmsTargets.Api)]
[CsmsOperator]
public sealed class TenantIsolation
{
    [ProtoTest]
    public async Task OneOperatorCannotReadAnotherOperatorsRows()
    {
        var own = Proto.Context.Resolve<CsmsOperator>();
        var other = await TenantProvisioning.ProvisionAsync(Proto.Context, "neighbor");

        // An open session on the test's own station, so the command scoping has a row to protect.
        Guid ownSession;
        using (var started = await Proto.Context.Rest(CsmsTargets.Api)
                   .Body(new { stationId = own.StationId, connectorId = 1 })
                   .PostAsync("/api/sessions"))
        {
            started.Should.HaveHttpStatus(HttpStatusCode.Created);
            ownSession = started.ReadAsJson<SessionResponse>()!.Id;
        }

        // The foreign rows: the neighbor's own station, and a seeded invoice of the other tenant.
        await DashboardSession.SignInAsync(SeededMonth.TenantB.LoginEmail, SeededMonth.TenantB.LoginPassword);
        Guid foreignStation;
        Guid foreignInvoice;
        using (var stations = await Proto.Context.Rest().GetAsync("/api/dashboard/stations"))
        {
            foreignStation = stations.ReadAsJson<DashboardStationResponse[]>()!
                .Single(row => row.Name == SeededMonth.TenantB.StationName).Id;
        }

        using (var invoices = await Proto.Context.Rest().GetAsync("/api/dashboard/invoices"))
        {
            foreignInvoice = invoices.ReadAsJson<InvoiceResponse[]>()!.First().Id;
        }

        // Signed in as the test's own operator, every foreign row is unknown.
        await DashboardSession.SignInAsync(own.LoginEmail, own.LoginPassword);
        using (var station = await Proto.Context.Rest().GetAsync($"/api/dashboard/stations/{foreignStation}"))
        {
            station.Should.HaveHttpStatus(HttpStatusCode.NotFound);
        }

        using (var sessions = await Proto.Context.Rest().GetAsync($"/api/dashboard/stations/{foreignStation}/sessions"))
        {
            sessions.Should.HaveHttpStatus(HttpStatusCode.NotFound);
        }

        using (var invoice = await Proto.Context.Rest().GetAsync($"/api/dashboard/invoices/{foreignInvoice}"))
        {
            invoice.Should.HaveHttpStatus(HttpStatusCode.NotFound);
        }

        using (var neighbor = await Proto.Context.Rest().GetAsync($"/api/dashboard/stations/{other.StationId}"))
        {
            neighbor.Should.HaveHttpStatus(HttpStatusCode.NotFound);
        }

        using (var stations = await Proto.Context.Rest().GetAsync("/api/dashboard/stations"))
        {
            var listed = stations.ReadAsJson<DashboardStationResponse[]>()!;
            Assert.That(
                listed.Select(row => row.Id),
                Is.EqualTo(new[] { own.StationId }),
                "the stations list carries only the signed-in tenant's station");
        }

        using (var tariffs = await Proto.Context.Rest().GetAsync("/api/dashboard/tariffs"))
        {
            var listed = tariffs.ReadAsJson<TariffResponse[]>()!;
            Assert.That(
                listed.Select(row => row.Id),
                Is.EqualTo(new[] { own.TariffId }),
                "the tariffs list carries only the signed-in tenant's tariff");
        }

        using (var invoices = await Proto.Context.Rest().GetAsync("/api/dashboard/invoices"))
        {
            Assert.That(invoices.ReadAsJson<InvoiceResponse[]>(), Is.Empty, "the test's tenant billed nothing");
        }

        // The mirror: the neighbor cannot reach the test's station either, including through the
        // operator command, which scopes to the tenant before it touches a charge point.
        await DashboardSession.SignInAsync(other.LoginEmail, other.LoginPassword);
        using (var station = await Proto.Context.Rest().GetAsync($"/api/dashboard/stations/{own.StationId}"))
        {
            station.Should.HaveHttpStatus(HttpStatusCode.NotFound);
        }

        var token = await DashboardSession.AntiforgeryTokenAsync();
        using var command = await Proto.Context.Rest()
            .Header("X-XSRF-TOKEN", token)
            .PostAsync($"/api/dashboard/sessions/{ownSession}/remote-stop");
        command.Should.HaveHttpStatus(HttpStatusCode.NotFound);
    }

    [ProtoTest]
    public async Task TheSeededTenantsCannotReadEachOthersInvoices()
    {
        await DashboardSession.SignInAsync(SeededMonth.TenantB.LoginEmail, SeededMonth.TenantB.LoginPassword);
        Guid foreignInvoice;
        using (var invoices = await Proto.Context.Rest().GetAsync("/api/dashboard/invoices"))
        {
            var listed = invoices.ReadAsJson<InvoiceResponse[]>()!;
            Assert.That(listed, Has.Length.EqualTo(SeededMonth.SessionsPerTenant), "the seeded month is intact");
            Assert.That(listed.Select(row => row.TenantId).Distinct(), Is.EqualTo(new[] { SeededMonth.TenantB.TenantId }));
            foreignInvoice = listed.First().Id;
        }

        await DashboardSession.SignInAsync(SeededMonth.TenantA.LoginEmail, SeededMonth.TenantA.LoginPassword);
        using (var invoices = await Proto.Context.Rest().GetAsync("/api/dashboard/invoices"))
        {
            var listed = invoices.ReadAsJson<InvoiceResponse[]>()!;
            Assert.That(listed, Has.Length.EqualTo(SeededMonth.SessionsPerTenant));
            Assert.That(listed.Select(row => row.Id), Does.Not.Contain(foreignInvoice));
        }

        using var invoice = await Proto.Context.Rest().GetAsync($"/api/dashboard/invoices/{foreignInvoice}");
        invoice.Should.HaveHttpStatus(HttpStatusCode.NotFound);
    }
}
