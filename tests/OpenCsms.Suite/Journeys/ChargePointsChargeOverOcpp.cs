namespace OpenCsms.Suite.Journeys;

using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenCsms.Application.Commands;
using OpenCsms.Contracts;
using OpenCsms.Protocol.Ocpp;
using OpenCsms.Suite.Devices;
using OpenCsms.Suite.Support;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Devices;
using ProtoTest.Json;
using ProtoTest.Messaging;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using BillingWorker = OpenCsms.Billing.Worker.Program;
using CsmsApi = OpenCsms.Api.Program;

/// <summary>
/// The gateway journeys, through the framework's device stack: a charge point reaches the in-process
/// API over <c>/ocpp/{chargePointId}</c>, boots, reports its connector, and runs one metered session to
/// its stop. The CSMS ends up with the same session the REST API would have written, and the billing
/// worker still sees its <c>session.ended</c> event, because the gateway publishes through the same
/// session-ending helper the REST endpoint uses. A second journey proves the other direction: the
/// operator's remote start and stop reach the connected charge point and wait for its own answer, and
/// the journeys after it pin the failure branches that wait can end in - not connected (409), a device
/// that refuses the call (502), a device that answers its own authorization decision such as Blocked
/// (200), and no answer within the configured timeout (504) - without changing the session the command
/// addressed. The
/// idle-fee journeys then bend the clock: a car that overstays the tariff's grace period pays per
/// started hour, and one that unplugs inside the grace period does not. The last journey runs two
/// sessions on one connector, whose register keeps counting across them, and proves each session bills
/// only the energy it added on top of the register it started from.
/// </summary>
[Application(CsmsTargets.Api)]
[CsmsOperator]
[RequiresDevice<AcCharger>]
public sealed class ChargePointsChargeOverOcpp
{
    private static readonly TimeSpan InvoiceTimeout = TimeSpan.FromSeconds(30);

    [ProtoTest]
    [RequiresTestClock]
    public async Task AChargePointBootsChargesAndStopsASession()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        var charger = Proto.Context.Devices(CsmsTargets.Chargers).For<AcCharger>(op.ChargePointId);

        // Act: the charge point does what a real one does at a connector.
        var boot = await charger.BootAsync();
        var heartbeat = await charger.HeartbeatAsync();
        await charger.ReportStatusAsync(1, ConnectorStatus.Preparing);
        await charger.ReportStatusAsync(1, ConnectorStatus.Charging);
        var started = await charger.StartTransactionAsync(
            connectorId: 1,
            idTag: "card-42",
            meterStartWh: 0,
            timestamp: SuiteClock.Instant);
        await charger.MeterValuesAsync(
            connectorId: 1,
            started.TransactionId,
            energyKwh: 22m,
            timestamp: SuiteClock.Instant);
        await charger.StopTransactionAsync(
            started.TransactionId,
            energyKwh: 22m,
            reason: "Local",
            timestamp: SuiteClock.Instant);
        await charger.ReportStatusAsync(1, ConnectorStatus.Available, timestamp: SuiteClock.Instant);

        // Assert: the protocol answers carry the injected clock and the subset's decisions.
        var station = await GetStationAsync(op.StationId);
        var connector = await GetConnectorAsync(op.StationId, connectorId: 1);
        var session = await GetSessionAsync(op.StationId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(boot.Status, Is.EqualTo(OcppRegistrationStatus.Accepted), "the station is registered for this charge point");
            Assert.That(boot.Interval, Is.EqualTo(300), "the CSMS asks for its configured heartbeat interval");
            Assert.That(boot.CurrentTime, Is.EqualTo(SuiteClock.Instant), "the gateway stamps answers from the injected clock");
            Assert.That(heartbeat.CurrentTime, Is.EqualTo(SuiteClock.Instant));
            Assert.That(started.TransactionId, Is.GreaterThan(0), "the CSMS assigned the transaction number");
            Assert.That(started.IdTagInfo.Status, Is.EqualTo(OcppAuthorizationStatus.Accepted));
        }

        // Assert: the API view holds what the charge point reported - the same shape REST writes.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(station.ChargePointId, Is.EqualTo(op.ChargePointId));
            Assert.That(station.LastSeenAtUtc, Is.EqualTo(SuiteClock.Instant), "boot and heartbeat marked the station seen");

            Assert.That(connector.ConnectorId, Is.EqualTo(1));
            Assert.That(connector.Status, Is.EqualTo(nameof(ConnectorStatus.Available)));
            Assert.That(connector.ErrorCode, Is.EqualTo(nameof(ChargePointErrorCode.NoError)));

            Assert.That(session.TransactionId, Is.EqualTo(started.TransactionId));
            Assert.That(session.ConnectorId, Is.EqualTo(1));
            Assert.That(session.IsOpen, Is.False);
            Assert.That(session.EnergyKwh, Is.EqualTo(22m));
            Assert.That(session.EndedAtUtc, Is.EqualTo(SuiteClock.Instant));
            Assert.That(session.StartedAtUtc, Is.EqualTo(SuiteClock.Instant));
        }

        // Assert: StopTransaction published the product's own session.ended, so the billing worker
        // produced the invoice without knowing the session came from a charger rather than REST.
        var message = await Proto.Context.Messaging().AwaitAsync(
            CsmsEvents.Exchange,
            candidate => CsmsMessages.IsInvoiceIssuedFor(candidate, session.Id),
            InvoiceTimeout);
        message.Should.MatchShape(new { sessionId = session.Id, tenantId = op.TenantId });
    }

    [ProtoTest]
    public async Task TheOperatorRemotelyStartsAndStopsAChargePoint()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        var charger = Proto.Context.Devices(CsmsTargets.Chargers).For<AcCharger>(op.ChargePointId);
        await charger.BootAsync();
        await charger.ReportStatusAsync(0, ConnectorStatus.Available);

        // Act: the operator asks the connected charge point to start. The REST request stays open
        // until the device answers, so the test receives the server-initiated call while it is in
        // flight and answers it - the duplex a real charger has.
        var startRequest = Proto.Context.Rest()
            .Body(new { idTag = "card-7", connectorId = 1 })
            .PostAsync($"/api/stations/{op.StationId}/remote-start");
        var startPayload = await charger.AnswerRemoteStartAsync();
        using (var response = await startRequest)
        {
            response.Should.HaveHttpStatus(HttpStatusCode.OK).Should.MatchShape(new { status = "Accepted" });
        }

        // A real charge point now runs the transaction OCPP prescribed, then the operator stops it.
        var started = await charger.StartTransactionAsync(
            connectorId: 1,
            idTag: startPayload.IdTag!,
            meterStartWh: 0,
            timestamp: SuiteClock.Instant);
        var session = await GetSessionAsync(op.StationId);

        var stopRequest = Proto.Context.Rest().PostAsync($"/api/sessions/{session.Id}/remote-stop");
        var stopPayload = await charger.AnswerRemoteStopAsync();
        using (var response = await stopRequest)
        {
            response.Should.HaveHttpStatus(HttpStatusCode.OK).Should.MatchShape(new { status = "Accepted" });
        }

        await charger.StopTransactionAsync(
            started.TransactionId,
            energyKwh: 5m,
            reason: "Remote",
            timestamp: SuiteClock.Instant);

        var stopped = await GetSessionAsync(op.StationId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(startPayload.IdTag, Is.EqualTo("card-7"));
            Assert.That(startPayload.ConnectorId, Is.EqualTo(1));
            Assert.That(stopPayload.TransactionId, Is.EqualTo(started.TransactionId), "the CSMS named the running transaction");
            Assert.That(stopped.IsOpen, Is.False, "the charge point's stop transaction ended the session");
            Assert.That(stopped.EnergyKwh, Is.EqualTo(5m));
        }
    }

    [ProtoTest]
    public async Task RemoteCommandsForADisconnectedChargePointAreConflicts()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();

        // Act: the operator asks a station whose charge point never connected to start, and, for a
        // session the CSMS holds, to stop. Resolving no charger keeps the charge point offline.
        using var start = await Proto.Context.Rest()
            .Body(new { idTag = "card-7", connectorId = 1 })
            .PostAsync($"/api/stations/{op.StationId}/remote-start");
        using var created = await Proto.Context.Rest()
            .Body(new { stationId = op.StationId, connectorId = 1 })
            .PostAsync("/api/sessions");
        var session = created.ReadRequired<SessionResponse>();
        using var stop = await Proto.Context.Rest().PostAsync($"/api/sessions/{session.Id}/remote-stop");

        // Assert: both answer 409 naming the charge point, and neither touched the store - the refused
        // start opened no session and the refused stop left its session open.
        var sessions = await GetSessionsAsync(op.StationId);
        var stillOpen = (await GetSessionAsync(op.StationId)).IsOpen;
        using (Assert.EnterMultipleScope())
        {
            start.Should.HaveHttpStatus(HttpStatusCode.Conflict);
            Assert.That(start.ReadRequired<string>("message"), Does.Contain(op.ChargePointId));
            stop.Should.HaveHttpStatus(HttpStatusCode.Conflict);
            Assert.That(stop.ReadRequired<string>("message"), Does.Contain(op.ChargePointId));
            Assert.That(sessions, Has.Count.EqualTo(1), "the refused start opened no session");
            Assert.That(stillOpen, Is.True, "the refused stop left the session open");
        }
    }

    [ProtoTest]
    public async Task ADeviceThatRefusesTheCallMakesTheRemoteCommandABadGateway()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        var charger = Proto.Context.Devices(CsmsTargets.Chargers).For<AcCharger>(op.ChargePointId);
        await charger.BootAsync();

        // Act: the device receives the CSMS's remote start and answers a call error - it cannot execute
        // the call. That is the gateway failure branch, not an authorization decision.
        var startRequest = Proto.Context.Rest()
            .Body(new { idTag = "card-7", connectorId = 1 })
            .PostAsync($"/api/stations/{op.StationId}/remote-start");
        var refusedStart = await charger.RefuseRemoteStartAsync(
            OcppErrorCodes.GenericError,
            "the connector is unavailable");
        using var start = await startRequest;
        var startDetail = start.ReadRequired<string>("detail");
        var afterStart = await GetSessionsAsync(op.StationId);

        using (Assert.EnterMultipleScope())
        {
            start.Should.HaveHttpStatus(HttpStatusCode.BadGateway);
            Assert.That(startDetail, Does.Contain("refused RemoteStartTransaction"));
            Assert.That(startDetail, Does.Contain("the connector is unavailable"));
            Assert.That(refusedStart.IdTag, Is.EqualTo("card-7"));
            Assert.That(refusedStart.ConnectorId, Is.EqualTo(1));
            Assert.That(afterStart, Is.Empty, "the refused start opened no session");
        }

        // The same device refuses to stop a running session: the operator gets 502 and the session
        // keeps running, because a refused stop must not end anything.
        var plugged = await charger.PlugInAsync(rfid: "card-42");
        var stored = await GetSessionAsync(op.StationId, plugged.TransactionId);
        var stopRequest = Proto.Context.Rest().PostAsync($"/api/sessions/{stored.Id}/remote-stop");
        var refusedStop = await charger.RefuseRemoteStopAsync(
            OcppErrorCodes.GenericError,
            "the connector is unavailable");
        using var stop = await stopRequest;
        var stopDetail = stop.ReadRequired<string>("detail");
        var stillOpen = (await GetSessionAsync(op.StationId)).IsOpen;

        using (Assert.EnterMultipleScope())
        {
            stop.Should.HaveHttpStatus(HttpStatusCode.BadGateway);
            Assert.That(stopDetail, Does.Contain("refused RemoteStopTransaction"));
            Assert.That(refusedStop.TransactionId, Is.EqualTo(plugged.TransactionId), "the CSMS named the running transaction");
            Assert.That(stillOpen, Is.True, "the refused stop left the session open");
        }
    }

    [ProtoTest]
    public async Task ABlockedRemoteStartIsTheDevicesOwnAnswer()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        var charger = Proto.Context.Devices(CsmsTargets.Chargers).For<AcCharger>(op.ChargePointId);
        await charger.BootAsync();

        // Act: the device answers the CSMS's call with its own authorization decision - here Blocked.
        // A well-formed decision is not a gateway failure: the operator reads it from the 200 body,
        // exactly as the happy path reads Accepted; the 502 branch is the call error test above.
        var startRequest = Proto.Context.Rest()
            .Body(new { idTag = "card-7", connectorId = 1 })
            .PostAsync($"/api/stations/{op.StationId}/remote-start");
        var blocked = await charger.AnswerRemoteStartAsync(OcppAuthorizationStatus.Blocked);
        using var start = await startRequest;
        var sessions = await GetSessionsAsync(op.StationId);

        start.Should.HaveHttpStatus(HttpStatusCode.OK).Should.MatchShape(new { status = "Blocked" });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(blocked.IdTag, Is.EqualTo("card-7"));
            Assert.That(sessions, Is.Empty, "the device declined, so no session started");
        }
    }

    [ProtoTest]
    public async Task AnUnansweredRemoteCommandTimesOutAtTheGateway()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        var charger = Proto.Context.Devices(CsmsTargets.Chargers).For<AcCharger>(op.ChargePointId);
        await charger.BootAsync();

        // Act: the device receives the remote start and does not answer it. The 504 arrives after the
        // API's effective remote timeout - two seconds while the suite hosts the test server, the
        // environment's own value against a running stack - and its body names that value, which
        // fails if the request used another one.
        var startRequest = Proto.Context.Rest()
            .Body(new { idTag = "card-7", connectorId = 1 })
            .PostAsync($"/api/stations/{op.StationId}/remote-start");
        var startCall = await charger.ReceiveCallAsync(
            OcppActions.RemoteStartTransaction,
            "the CSMS starts a transaction remotely");
        using var start = await startRequest;
        var startDetail = start.ReadRequired<string>("detail");
        var afterStart = await GetSessionsAsync(op.StationId);
        var timeout = RemoteCallTimeoutSeconds();

        using (Assert.EnterMultipleScope())
        {
            start.Should.HaveHttpStatus(HttpStatusCode.GatewayTimeout);
            Assert.That(startDetail, Does.Contain($"did not answer RemoteStartTransaction within {timeout}s"));
            Assert.That(
                OcppJson.ReadPayload<RemoteStartTransactionRequest>(startCall.Payload).ConnectorId,
                Is.EqualTo(1),
                "the device saw the call it did not answer");
            Assert.That(afterStart, Is.Empty, "the timed-out start opened no session");
        }

        // The stop timeout: the session runs, the device takes the call and never answers, and the
        // session must still be running - a lost stop answer is not a stop.
        var plugged = await charger.PlugInAsync(rfid: "card-42");
        var stored = await GetSessionAsync(op.StationId, plugged.TransactionId);
        var stopRequest = Proto.Context.Rest().PostAsync($"/api/sessions/{stored.Id}/remote-stop");
        var stopCall = await charger.ReceiveCallAsync(
            OcppActions.RemoteStopTransaction,
            "the CSMS stops a transaction remotely");
        using var stop = await stopRequest;
        var stopDetail = stop.ReadRequired<string>("detail");
        var stillOpen = (await GetSessionAsync(op.StationId)).IsOpen;

        using (Assert.EnterMultipleScope())
        {
            stop.Should.HaveHttpStatus(HttpStatusCode.GatewayTimeout);
            Assert.That(stopDetail, Does.Contain($"did not answer RemoteStopTransaction within {timeout}s"));
            Assert.That(
                OcppJson.ReadPayload<RemoteStopTransactionRequest>(stopCall.Payload).TransactionId,
                Is.EqualTo(plugged.TransactionId),
                "the CSMS named the running transaction");
            Assert.That(stillOpen, Is.True, "the timed-out stop must not end the session");
        }
    }

    [ProtoTest]
    [RequiresTestClock]
    [RequiresCapability(
        ProtoCapabilityKinds.Broker,
        Reason = "The journey awaits invoice.issued on the broker; configure ProtoTest:Messaging:RabbitMq:ConnectionString.")]
    [RequiresWorker<BillingWorker>]
    public async Task An_overstaying_car_is_charged_an_idle_fee()
    {
        // Arrange: the attribute registered the tenant, tariff and station this test bills against.
        var op = Proto.Context.Resolve<CsmsOperator>();
        var charger = Proto.Context.Devices(CsmsTargets.Chargers).For<AcCharger>(op.ChargePointId);
        await charger.BootAsync();

        // Act: a driver charges 22 kWh, the car stays plugged in, and five hours after the last meter
        // value the driver unplugs - the test advances the injected clock instead of sleeping.
        await charger.PlugInAsync(rfid: "card-42");
        await charger.MeterValuesAsync(energyKwh: 22m);
        Proto.Context.Clock.Advance(TimeSpan.FromHours(5));
        await charger.UnplugAsync();

        // Assert: the worker billed the overstay. The tariff is 0.40 per kWh, a 1.50 start fee and
        // 2.00 per started idle hour after a 10-minute grace, so the invoice is
        // 8.80 + 1.50 + 5 * 2.00 = 20.30. The shape assertion follows the spec's; the scope below
        // pins the rule's own numbers, which fail if the gateway stamped the run clock instead of
        // this test's.
        using var invoice = await ReadIssuedInvoiceAsync(op.StationId);
        invoice.Should.HaveHttpStatus(HttpStatusCode.OK)
            .Should.MatchShape(new
            {
                energyKwh = 22m,
                idleFeeAmount = JsonValue.GreaterThan(0m),
                total = JsonValue.GreaterThan(0m)
            });

        var billed = invoice.ReadRequired<InvoiceResponse>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(billed.IdleHours, Is.EqualTo(5), "4h50m beyond the grace period is five started hours");
            Assert.That(billed.IdleFeeAmount, Is.EqualTo(10.00m));
            Assert.That(billed.Total, Is.EqualTo(20.30m));
        }
    }

    [ProtoTest]
    [RequiresTestClock]
    [RequiresCapability(
        ProtoCapabilityKinds.Broker,
        Reason = "The journey awaits invoice.issued on the broker; configure ProtoTest:Messaging:RabbitMq:ConnectionString.")]
    [RequiresWorker<BillingWorker>]
    public async Task ACarThatUnplugsBeforeTheGracePeriodPaysNoIdleFee()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        var charger = Proto.Context.Devices(CsmsTargets.Chargers).For<AcCharger>(op.ChargePointId);
        await charger.BootAsync();

        // Act: the same 22 kWh, but the driver unplugs five minutes in - inside the 10-minute grace.
        await charger.PlugInAsync(rfid: "card-43");
        await charger.MeterValuesAsync(energyKwh: 22m);
        Proto.Context.Clock.Advance(TimeSpan.FromMinutes(5));
        await charger.UnplugAsync();

        // Assert: energy and the start fee only; no idle time was billed.
        using var invoice = await ReadIssuedInvoiceAsync(op.StationId);
        invoice.Should.HaveHttpStatus(HttpStatusCode.OK);

        var billed = invoice.ReadRequired<InvoiceResponse>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(billed.IdleHours, Is.Zero);
            Assert.That(billed.IdleFeeAmount, Is.Zero);
            Assert.That(billed.Total, Is.EqualTo(10.30m));
        }
    }

    [ProtoTest]
    [RequiresTestClock]
    [RequiresCapability(
        ProtoCapabilityKinds.Broker,
        Reason = "The journey awaits invoice.issued on the broker; configure ProtoTest:Messaging:RabbitMq:ConnectionString.")]
    [RequiresWorker<BillingWorker>]
    public async Task ASecondSessionOnAConnectorBillsItsOwnEnergyOnly()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        var charger = Proto.Context.Devices(CsmsTargets.Chargers).For<AcCharger>(op.ChargePointId);
        await charger.BootAsync();

        // Act: a connector's first session ends at a 22 kWh register...
        var first = await charger.PlugInAsync(rfid: "card-41");
        await charger.MeterValuesAsync(energyKwh: 22m);
        Proto.Context.Clock.Advance(TimeSpan.FromMinutes(5));
        await charger.UnplugAsync();

        var firstSession = await GetSessionAsync(op.StationId, first.TransactionId);
        using var firstInvoice = await ReadIssuedInvoiceAsync(firstSession);
        var billedFirst = firstInvoice.ReadRequired<InvoiceResponse>();

        // ...and the next car plugs into the same connector. A real meter keeps counting, so the
        // charge point starts the second transaction at the 22 kWh register and adds 5 kWh.
        var second = await charger.PlugInAsync(rfid: "card-42");
        Assert.That(
            second.MeterStartWh,
            Is.EqualTo(22_000),
            "the connector's register carries into the next session");
        await charger.MeterValuesAsync(energyKwh: 5m);
        Proto.Context.Clock.Advance(TimeSpan.FromMinutes(5));
        await charger.UnplugAsync();

        var secondSession = await GetSessionAsync(op.StationId, second.TransactionId);
        using var secondInvoice = await ReadIssuedInvoiceAsync(secondSession);
        var billedSecond = secondInvoice.ReadRequired<InvoiceResponse>();

        // Assert: each session bills only the energy it added - 22 kWh, then 5 kWh - never the
        // 27 kWh the register stands at when the second car leaves.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(billedFirst.EnergyKwh, Is.EqualTo(22m));
            Assert.That(billedFirst.Total, Is.EqualTo(10.30m));

            Assert.That(secondSession.EnergyKwh, Is.EqualTo(5m), "the session holds its own energy, not the register");
            Assert.That(billedSecond.EnergyKwh, Is.EqualTo(5m), "the second session bills 5 kWh, not the 27 kWh register");
            Assert.That(billedSecond.Total, Is.EqualTo(3.50m), "5 kWh at 0.40 plus the 1.50 start fee");
        }
    }

    /// <summary>
    /// The API's effective remote-call timeout: while the suite hosts the application in-process,
    /// the value bound by the test server; against a running environment, the product's own
    /// <c>Ocpp:RemoteCallTimeoutSeconds</c> key (which the environment exports to both sides) with
    /// the product's default when neither is set.
    /// </summary>
    private static int RemoteCallTimeoutSeconds()
    {
        if (Proto.Context.TryServerFactory<CsmsApi>() is { } server)
        {
            return server.Services.GetRequiredService<IOptions<RemoteCommandOptions>>().Value.RemoteCallTimeoutSeconds;
        }

        var configured = Proto.Context.Configuration["Ocpp:RemoteCallTimeoutSeconds"];
        return int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? seconds
            : new RemoteCommandOptions().RemoteCallTimeoutSeconds;
    }

    /// <summary>
    /// Waits for the invoice the billing worker issues for the station's session and reads it over
    /// REST: the <c>invoice.issued</c> event names the invoice id the API addresses, and the read
    /// afterwards is the durable check.
    /// </summary>
    private static async Task<RestResponse> ReadIssuedInvoiceAsync(Guid stationId)
        => await ReadIssuedInvoiceAsync(await GetSessionAsync(stationId));

    private static async Task<RestResponse> ReadIssuedInvoiceAsync(SessionResponse session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var message = await Proto.Context.Messaging().AwaitAsync(
            CsmsEvents.Exchange,
            candidate => CsmsMessages.IsInvoiceIssuedFor(candidate, session.Id),
            InvoiceTimeout);
        var issued = message.ReadRequired<InvoiceIssued>();

        return await Proto.Context.Rest().GetAsync($"/api/invoices/{issued.InvoiceId}");
    }

    private static async Task<StationResponse> GetStationAsync(Guid stationId)
    {
        using var response = await Proto.Context.Rest().GetAsync($"/api/stations/{stationId}");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        return response.ReadAsJson<StationResponse>()!;
    }

    private static async Task<ConnectorResponse> GetConnectorAsync(Guid stationId, int connectorId)
    {
        using var response = await Proto.Context.Rest().GetAsync($"/api/stations/{stationId}/connectors");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        var connectors = response.ReadAsJson<List<ConnectorResponse>>()!;
        return connectors.Single(connector => connector.ConnectorId == connectorId);
    }

    private static async Task<SessionResponse> GetSessionAsync(Guid stationId)
    {
        var sessions = await GetSessionsAsync(stationId);
        return sessions[0];
    }

    private static async Task<SessionResponse> GetSessionAsync(Guid stationId, int transactionId)
    {
        var sessions = await GetSessionsAsync(stationId);
        return sessions.Single(session => session.TransactionId == transactionId);
    }

    private static async Task<List<SessionResponse>> GetSessionsAsync(Guid stationId)
    {
        using var response = await Proto.Context.Rest().GetAsync($"/api/stations/{stationId}/sessions");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        return response.ReadAsJson<List<SessionResponse>>()!;
    }
}
