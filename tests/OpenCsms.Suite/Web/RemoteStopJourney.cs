namespace OpenCsms.Suite.Web;

using System.Net;
using OpenCsms.Contracts;
using OpenCsms.Protocol.Ocpp;
using OpenCsms.Suite.Devices;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.Devices;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.Web;
using ProtoTest.Web.Playwright;
using BillingWorker = OpenCsms.Billing.Worker.Program;

/// <summary>
/// The dashboard remote-stop journey: the charge point behind the loopback listener is connected,
/// so the operator's stop button on the open session reaches it, the device answers with the running
/// transaction, and the device's own stop transaction ends the session - the UI path of what the API
/// journey covers against the machine endpoint. The device client hangs off the dashboard
/// application on purpose: the in-process journeys keep the test server with the test's clock, while
/// this journey needs the instance the browser talks to, over a real socket.
/// </summary>
[Application(CsmsTargets.Dashboard)]
[CsmsOperator]
[RequiresDevice<AcCharger>]
[WebSession("Default", Application = CsmsTargets.Dashboard, DiscoverRoutes = true)]
[LoginAs<CsmsOperatorLogin>("operator")]
[RequiresPlaywrightBrowser]
[RequiresDashboardBuild]
public sealed class RemoteStopJourney
{
    [ProtoTest]
    public async Task TheOperatorStopsAConnectedChargePointFromTheStationScreen()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        var charger = Proto.Context.Devices(CsmsTargets.DashboardChargers).For<AcCharger>(op.ChargePointId);
        await charger.BootAsync();
        var plugged = await charger.PlugInAsync(rfid: "card-42");

        // The station timeline carries the open session with the operator's stop button.
        var stations = Proto.Context.Web().Page<StationsPage>();
        await stations.OpenAsync("/");
        await stations.Station(op.StationName).Link.ClickAsync();
        var station = Proto.Context.Web().Page<StationDetailPage>();
        await station.Title.Should.HaveTextAsync(op.StationName, OpenCsmsDashboard.Wait);
        var row = station.Sessions.Matching(By.HasText("Open"), "Session[open]");

        // Act: the operator stops the session from the screen. The dashboard's request stays open
        // until the device answers, so the test answers while the click's fetch is in flight - the
        // duplex a real charge point has.
        var answer = charger.AnswerRemoteStopAsync();
        await row.Stop.ClickAsync();
        var stop = await answer;
        await station.StopResult.Should.HaveTextAsync(
            "The charge point answered Accepted.", OpenCsmsDashboard.Wait);

        // The device ends the transaction it was asked to stop, and the timeline shows it ended.
        await charger.StopTransactionAsync(stop.TransactionId, energyKwh: 5m, reason: "Remote");
        await charger.ReportStatusAsync(1, ConnectorStatus.Available);
        await station.OpenAsync("/");
        await stations.Station(op.StationName).Link.ClickAsync();
        await station.Title.Should.HaveTextAsync(op.StationName, OpenCsmsDashboard.Wait);
        var ended = station.Sessions.Matching(By.HasText("5 kWh"), "Session[5 kWh]");
        await ended.State.Should.HaveTextAsync("Ended", OpenCsmsDashboard.Wait);

        // Assert: the dashboard named the running transaction, and the store holds the session the
        // device's stop ended - the same shape the API journey proves through the machine endpoint.
        var stored = await GetSessionAsync(op.StationId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stop.TransactionId, Is.EqualTo(plugged.TransactionId), "the dashboard named the running transaction");
            Assert.That(stored.IsOpen, Is.False, "the device's stop transaction ended the session");
            Assert.That(stored.EnergyKwh, Is.EqualTo(5m));
            Assert.That(stored.TransactionId, Is.EqualTo(plugged.TransactionId));
        }
    }

    private static async Task<SessionResponse> GetSessionAsync(Guid stationId)
    {
        using var response = await Proto.Context.Rest(CsmsTargets.Api).GetAsync($"/api/stations/{stationId}/sessions");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        return response.ReadAsJson<List<SessionResponse>>()!.Single();
    }
}
