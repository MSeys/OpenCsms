namespace OpenCsms.Suite.Api;

using System.Net;
using OpenCsms.Contracts;
using OpenCsms.Domain;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using BillingWorker = OpenCsms.Billing.Worker.Program;

/// <summary>
/// The tariff repricing contracts: who may reprice, what the API refuses, and what a repricing
/// means for billing. Reads stay open to both roles; the PUT is the operator admin's, behind the
/// anti-forgery token the SPA sends, and a tariff of another tenant answers 404 exactly like a
/// missing one. A negative value in any of the four prices is 400 with the tariff provably
/// unchanged. Stored invoices keep the prices they were billed at - only new sessions bill at
/// the new ones.
/// </summary>
[Application(CsmsTargets.Api)]
public sealed class TariffEditingContracts
{
    [ProtoTest]
    public async Task RepricingRefusesAnonymousCallers()
    {
        using var response = await Proto.Context.Rest()
            .Body(new
            {
                energyPricePerKwh = 0.55m,
                startFee = 1.50m,
                idleFeePerHour = 2.00m,
                idleGracePeriod = TimeSpan.FromMinutes(10)
            })
            .PutAsync($"/api/dashboard/tariffs/{Guid.NewGuid()}");

        response.Should.HaveHttpStatus(HttpStatusCode.Unauthorized);
    }

    [ProtoTest]
    [CsmsOperator]
    [CsmsViewer]
    public async Task AViewerCannotReprice()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        var viewer = Proto.Context.Resolve<CsmsViewer>();
        await DashboardSession.SignInAsync(viewer.LoginEmail, viewer.LoginPassword);

        var token = await DashboardSession.AntiforgeryTokenAsync();
        using var response = await Proto.Context.Rest()
            .Header("X-XSRF-TOKEN", token)
            .Body(new
            {
                energyPricePerKwh = 0.55m,
                startFee = 1.50m,
                idleFeePerHour = 2.00m,
                idleGracePeriod = TimeSpan.FromMinutes(10)
            })
            .PutAsync($"/api/dashboard/tariffs/{op.TariffId}");

        response.Should.HaveHttpStatus(HttpStatusCode.Forbidden);
    }

    [ProtoTest]
    [CsmsOperator]
    public async Task RepricingNeedsTheAntiforgeryToken()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        await DashboardSession.SignInAsync(op.LoginEmail, op.LoginPassword);

        using var response = await Proto.Context.Rest()
            .Body(new
            {
                energyPricePerKwh = 0.55m,
                startFee = 1.50m,
                idleFeePerHour = 2.00m,
                idleGracePeriod = TimeSpan.FromMinutes(10)
            })
            .PutAsync($"/api/dashboard/tariffs/{op.TariffId}");

        response.Should.HaveHttpStatus(HttpStatusCode.BadRequest);
    }

    [ProtoTest]
    [CsmsOperator]
    public async Task RepricingAnotherTenantsTariffIsNotFound()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        var other = await TenantProvisioning.ProvisionAsync(Proto.Context, "foreign");
        await DashboardSession.SignInAsync(op.LoginEmail, op.LoginPassword);

        var token = await DashboardSession.AntiforgeryTokenAsync();
        using var response = await Proto.Context.Rest()
            .Header("X-XSRF-TOKEN", token)
            .Body(new
            {
                energyPricePerKwh = 0.55m,
                startFee = 1.50m,
                idleFeePerHour = 2.00m,
                idleGracePeriod = TimeSpan.FromMinutes(10)
            })
            .PutAsync($"/api/dashboard/tariffs/{other.TariffId}");

        response.Should.HaveHttpStatus(HttpStatusCode.NotFound);
    }

    [ProtoTest]
    [CsmsOperator]
    public async Task RepricingRefusesNegativePricesAndChangesNothing()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        await DashboardSession.SignInAsync(op.LoginEmail, op.LoginPassword);

        var token = await DashboardSession.AntiforgeryTokenAsync();
        using (var response = await Proto.Context.Rest()
                   .Header("X-XSRF-TOKEN", token)
                   .Body(new
                   {
                       energyPricePerKwh = -0.55m,
                       startFee = 1.50m,
                       idleFeePerHour = 2.00m,
                       idleGracePeriod = TimeSpan.FromMinutes(10)
                   })
                   .PutAsync($"/api/dashboard/tariffs/{op.TariffId}"))
        {
            response.Should.HaveHttpStatus(HttpStatusCode.BadRequest);
        }

        using var tariffs = await Proto.Context.Rest().GetAsync("/api/dashboard/tariffs");
        var listed = tariffs.ReadAsJson<TariffResponse[]>()!;
        var tariff = listed.Single(row => row.Id == op.TariffId);
        Assert.That(tariff.EnergyPricePerKwh, Is.EqualTo(0.40m), "the refused repricing changed nothing");
    }

    [ProtoTest]
    [CsmsOperator]
    [TestCase("startFee", -1.50)]
    [TestCase("idleFeePerHour", -2.00)]
    public async Task RepricingRefusesNegativeFeesAndChangesNothing(string field, decimal value)
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        await DashboardSession.SignInAsync(op.LoginEmail, op.LoginPassword);

        var token = await DashboardSession.AntiforgeryTokenAsync();
        var body = new Dictionary<string, object>
        {
            ["energyPricePerKwh"] = 0.55m,
            ["startFee"] = 1.50m,
            ["idleFeePerHour"] = 2.00m,
            ["idleGracePeriod"] = TimeSpan.FromMinutes(10)
        };
        body[field] = value;
        using (var response = await Proto.Context.Rest()
                   .Header("X-XSRF-TOKEN", token)
                   .Body(body)
                   .PutAsync($"/api/dashboard/tariffs/{op.TariffId}"))
        {
            response.Should.HaveHttpStatus(HttpStatusCode.BadRequest);
        }

        await TariffIsUnchangedAsync(op.TariffId);
    }

    [ProtoTest]
    [CsmsOperator]
    public async Task RepricingRefusesANegativeGracePeriodAndChangesNothing()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        await DashboardSession.SignInAsync(op.LoginEmail, op.LoginPassword);

        var token = await DashboardSession.AntiforgeryTokenAsync();
        using (var response = await Proto.Context.Rest()
                   .Header("X-XSRF-TOKEN", token)
                   .Body(new
                   {
                       energyPricePerKwh = 0.55m,
                       startFee = 1.50m,
                       idleFeePerHour = 2.00m,
                       idleGracePeriod = TimeSpan.FromMinutes(-10)
                   })
                   .PutAsync($"/api/dashboard/tariffs/{op.TariffId}"))
        {
            response.Should.HaveHttpStatus(HttpStatusCode.BadRequest);
        }

        await TariffIsUnchangedAsync(op.TariffId);
    }

    [ProtoTest]
    [CsmsOperator]
    [RequiresCapability(
        ProtoCapabilityKinds.Broker,
        Reason = "The test awaits invoice.issued on the broker; configure ProtoTest:Messaging:RabbitMq:ConnectionString.")]
    [RequiresWorker<BillingWorker>]
    public async Task NewSessionsBillAtTheNewPricesWhileStoredInvoicesKeepTheirs()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();

        // A session billed before the repricing, at 0.40/kWh.
        var first = await RunSessionAsync(op.StationId, 1, 22m);
        await AwaitInvoiceAsync(first.Id);

        // The repricing itself, through the dashboard's operator endpoint.
        await DashboardSession.SignInAsync(op.LoginEmail, op.LoginPassword);
        var token = await DashboardSession.AntiforgeryTokenAsync();
        using (var repriced = await Proto.Context.Rest()
                   .Header("X-XSRF-TOKEN", token)
                   .Body(new
                   {
                       energyPricePerKwh = 0.55m,
                       startFee = 1.50m,
                       idleFeePerHour = 2.00m,
                       idleGracePeriod = TimeSpan.FromMinutes(10)
                   })
                   .PutAsync($"/api/dashboard/tariffs/{op.TariffId}"))
        {
            repriced.Should.HaveHttpStatus(HttpStatusCode.OK);
            repriced.Should.MatchShape(new { id = op.TariffId, energyPricePerKwh = 0.55m });
        }

        // A session billed after it, at 0.55/kWh.
        var second = await RunSessionAsync(op.StationId, 2, 22m);
        var issued = await AwaitInvoiceAsync(second.Id);
        var secondInvoice = issued.ReadRequired<InvoiceIssued>();

        using var firstInvoice = await Proto.Context.Rest().GetAsync($"/api/sessions/{first.Id}/invoice");
        var stored = firstInvoice.ReadAsJson<InvoiceResponse>()!;

        Assert.Multiple(() =>
        {
            Assert.That(secondInvoice.Total, Is.EqualTo(13.60m), "22 kWh at 0.55 plus the 1.50 start fee");
            Assert.That(stored.EnergyAmount, Is.EqualTo(8.80m), "the stored invoice keeps its old prices");
            Assert.That(stored.Total, Is.EqualTo(10.30m));
        });
    }

    private static async Task<SessionResponse> RunSessionAsync(Guid stationId, int connectorId, decimal energyKwh)
    {
        using (var started = await Proto.Context.Rest()
                   .Body(new { stationId, connectorId })
                   .PostAsync("/api/sessions"))
        {
            started.Should.HaveHttpStatus(HttpStatusCode.Created);
            var session = started.ReadAsJson<SessionResponse>()!;

            using (var meter = await Proto.Context.Rest()
                       .Body(new { totalKwh = energyKwh })
                       .PostAsync($"/api/sessions/{session.Id}/meter-values"))
            {
                meter.Should.HaveHttpStatus(HttpStatusCode.OK);
            }

            using var ended = await Proto.Context.Rest().PostAsync($"/api/sessions/{session.Id}/end");
            ended.Should.HaveHttpStatus(HttpStatusCode.OK);
            return session;
        }
    }

    private static Task<ProtoMessage> AwaitInvoiceAsync(Guid sessionId)
        => Proto.Context.Messaging().AwaitAsync(
            CsmsEvents.Exchange,
            candidate => CsmsMessages.IsInvoiceIssuedFor(candidate, sessionId),
            TimeSpan.FromSeconds(30));

    /// <summary>
    /// Reads the tariff back and asserts the refused repricing changed none of its four prices.
    /// </summary>
    private static async Task TariffIsUnchangedAsync(Guid tariffId)
    {
        using var tariffs = await Proto.Context.Rest().GetAsync("/api/dashboard/tariffs");
        var tariff = tariffs.ReadAsJson<TariffResponse[]>()!.Single(row => row.Id == tariffId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tariff.EnergyPricePerKwh, Is.EqualTo(0.40m), "the refused repricing changed nothing");
            Assert.That(tariff.StartFee, Is.EqualTo(1.50m));
            Assert.That(tariff.IdleFeePerHour, Is.EqualTo(2.00m));
            Assert.That(tariff.IdleGracePeriod, Is.EqualTo(TimeSpan.FromMinutes(10)));
        }
    }
}
