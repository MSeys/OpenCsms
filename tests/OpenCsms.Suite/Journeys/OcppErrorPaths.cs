namespace OpenCsms.Suite.Journeys;

using System.Net;
using OpenCsms.Api;
using OpenCsms.Contracts;
using OpenCsms.Domain.Ocpp;
using OpenCsms.Suite.Devices;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.Devices;
using ProtoTest.Messaging;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using BillingWorker = OpenCsms.Billing.Worker.Program;

/// <summary>
/// The M2 error paths, driven through the simulator exactly as the happy journeys are: a stop the
/// charge point resends is answered without ending or billing anything twice; meter values the
/// gateway cannot read, or that move backwards, are refused with the OCPP error code their rule
/// names and leave the session as it was; a charge point no station is registered for is rejected at
/// boot and refused every other call; and the calls outside the documented subset, or outside the
/// station's connectors, are refused without touching the store. Each refusal pins the code the
/// README documents.
/// </summary>
[Application(CsmsTargets.Api)]
[RequiresDevice<AcCharger>]
public sealed class OcppErrorPaths
{
    [ProtoTest]
    [CsmsOperator]
    [RequiresCapability(
        ProtoCapabilityKinds.Broker,
        Reason = "The duplicate stop is proved against the worker's invoice; configure ProtoTest:Messaging:RabbitMq:ConnectionString.")]
    [RequiresWorker<BillingWorker>]
    public async Task ADuplicateStopTransactionEndsNothingTwice()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        var charger = Proto.Context.Devices().For<AcCharger>(op.ChargePointId);
        await charger.BootAsync();
        var session = await charger.PlugInAsync(rfid: "card-42");
        await charger.MeterValuesAsync(energyKwh: 22m);
        await charger.UnplugAsync();

        // The first stop's invoice, awaited the way a client sees it, and the session it billed.
        var endedSession = await GetSessionAsync(op.StationId, session.TransactionId);
        using var firstInvoice = await ReadIssuedInvoiceAsync(endedSession);
        var first = firstInvoice.ReadRequired<InvoiceResponse>();

        // Act: a minute later the charge point resends the stop it lost the answer to. A session that
        // were ended a second time would carry the later instant, not the first stop's.
        Proto.Context.Clock.Advance(TimeSpan.FromMinutes(1));
        var resent = await charger.StopTransactionAsync(
            session.TransactionId,
            energyKwh: 22m,
            reason: "Local",
            timestamp: Proto.Context.Clock.GetUtcNow());

        // Assert: answered, not refused, and the store is exactly where the first stop left it.
        var stored = await GetSessionAsync(op.StationId, session.TransactionId);
        using var secondInvoice = await Proto.Context.Rest().GetAsync($"/api/invoices/{first.Id}");
        secondInvoice.Should.HaveHttpStatus(HttpStatusCode.OK);
        var again = secondInvoice.ReadRequired<InvoiceResponse>();
        var invoiceRows = await CsmsDatabase.CountInvoicesAsync(Proto.Context, endedSession.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                resent.IdTagInfo!.Status,
                Is.EqualTo(OcppAuthorizationStatus.Accepted),
                "a stop for an ended transaction is answered, not refused");
            Assert.That(
                stored.EndedAtUtc,
                Is.EqualTo(SuiteClock.Instant),
                "the duplicate stop neither ended the session again nor restamped it at the later clock");
            Assert.That(stored.EnergyKwh, Is.EqualTo(22m));
            Assert.That(
                invoiceRows,
                Is.EqualTo(1),
                "the duplicate stop published nothing the worker could bill twice");
            Assert.That(again.Id, Is.EqualTo(first.Id), "the invoice is still the one the first stop issued");
            Assert.That(again.Total, Is.EqualTo(10.30m));
        }
    }

    [ProtoTest]
    [CsmsOperator]
    public async Task ANonNumericEnergySampleIsRefused()
        => await RefusedMeterValueLeavesTheSessionUnchangedAsync(
            new SampledValue("nope", Measurand: OcppEnergy.ActiveImportRegister, Unit: "Wh"),
            OcppErrorCodes.FormationViolation);

    [ProtoTest]
    [CsmsOperator]
    public async Task AMeterValueWithoutAnEnergySampleIsRefused()
        => await RefusedMeterValueLeavesTheSessionUnchangedAsync(
            new SampledValue("7", Measurand: "Power.Active.Import", Unit: "W"),
            OcppErrorCodes.FormationViolation);

    [ProtoTest]
    [CsmsOperator]
    public async Task AReadingBelowTheLastOneIsRefused()
        => await RefusedMeterValueLeavesTheSessionUnchangedAsync(
            new SampledValue("10000", Measurand: OcppEnergy.ActiveImportRegister, Unit: "Wh"),
            OcppErrorCodes.PropertyConstraintViolation);

    [ProtoTest]
    public async Task AnUnknownChargePointBootsRejectedAndIsRefusedOtherCalls()
    {
        // No station is registered for this identity; the simulator still reaches the gateway, as a
        // real charger on a shared network would.
        var charger = Proto.Context.Devices().For<AcCharger>(Proto.Context.UniqueName("cp-unknown"));

        var boot = await charger.BootAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(boot.Status, Is.EqualTo(OcppRegistrationStatus.Rejected), "an unknown identity is rejected");
            Assert.That(boot.Interval, Is.EqualTo(300), "the refused boot still gets the heartbeat interval");
        }

        // Other calls have no station to act on; the gateway answers InternalError and names the fix.
        var error = await charger.RefusedAsync(OcppActions.Heartbeat, new HeartbeatRequest());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.ErrorCode, Is.EqualTo(OcppErrorCodes.InternalError));
            Assert.That(error.Description, Does.Contain("No station is registered"));
        }
    }

    [ProtoTest]
    [TestCase(OcppActions.Heartbeat)]
    [TestCase(OcppActions.StatusNotification)]
    [TestCase(OcppActions.StartTransaction)]
    [TestCase(OcppActions.MeterValues)]
    [TestCase(OcppActions.StopTransaction)]
    public async Task AnUnknownChargePointIsRefusedEveryOtherCall(string action)
    {
        var charger = Proto.Context.Devices().For<AcCharger>(Proto.Context.UniqueName("cp-unknown"));

        // The identity is checked before the payload is read, so the request shape does not matter.
        var error = await charger.RefusedAsync(action, new { });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.ErrorCode, Is.EqualTo(OcppErrorCodes.InternalError));
            Assert.That(error.Description, Does.Contain("No station is registered"));
        }
    }

    [ProtoTest]
    [CsmsOperator]
    [TestCase("Authorize")]
    [TestCase("DataTransfer")]
    [TestCase("Reset")]
    [TestCase("UnlockConnector")]
    [TestCase("UpdateFirmware")]
    [TestCase("FirmwareStatusNotification")]
    [TestCase("DiagnosticsStatusNotification")]
    [TestCase("SetChargingProfile")]
    public async Task AnActionOutsideTheSubsetIsRefused(string action)
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        var charger = Proto.Context.Devices().For<AcCharger>(op.ChargePointId);
        await charger.BootAsync();
        var before = await GetStationAsync(op.StationId);

        var error = await charger.RefusedAsync(action, new { });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.ErrorCode, Is.EqualTo(OcppErrorCodes.NotImplemented));
            Assert.That(error.Description, Does.Contain(action));
            Assert.That(
                (await GetStationAsync(op.StationId)).LastSeenAtUtc,
                Is.EqualTo(before.LastSeenAtUtc),
                "the refusal touched no station state");
            Assert.That(await GetSessionsAsync(op.StationId), Is.Empty);
            Assert.That(await GetConnectorsAsync(op.StationId), Is.Empty);
        }
    }

    [ProtoTest]
    [CsmsOperator]
    public async Task AConnectorOutsideTheStationIsRefused()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        var charger = Proto.Context.Devices().For<AcCharger>(op.ChargePointId);
        await charger.BootAsync();

        var status = await charger.RefusedAsync(
            OcppActions.StatusNotification,
            new StatusNotificationRequest(
                3,
                ConnectorStatus.Available,
                ChargePointErrorCode.NoError,
                Timestamp: SuiteClock.Instant));
        var start = await charger.RefusedAsync(
            OcppActions.StartTransaction,
            new StartTransactionRequest(3, "card-42", 0, SuiteClock.Instant));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.ErrorCode, Is.EqualTo(OcppErrorCodes.PropertyConstraintViolation));
            Assert.That(start.ErrorCode, Is.EqualTo(OcppErrorCodes.PropertyConstraintViolation));
            Assert.That(await GetSessionsAsync(op.StationId), Is.Empty, "no session was opened for connector 3");
            Assert.That(await GetConnectorsAsync(op.StationId), Is.Empty, "no connector 3 was recorded");
        }
    }

    /// <summary>
    /// The malformed meter value in one place: the session first records a good 22 kWh reading, the
    /// sample under test is refused with <paramref name="expectedErrorCode"/>, and the session must
    /// still be open at 22 kWh afterwards.
    /// </summary>
    private static async Task RefusedMeterValueLeavesTheSessionUnchangedAsync(
        SampledValue malformed,
        string expectedErrorCode)
    {
        var op = Proto.Context.Resolve<CsmsOperator>();
        var charger = Proto.Context.Devices().For<AcCharger>(op.ChargePointId);
        await charger.BootAsync();
        var session = await charger.PlugInAsync(rfid: "card-42");
        await charger.MeterValuesAsync(energyKwh: 22m);
        var before = await GetSessionAsync(op.StationId);

        var error = await charger.RefusedAsync(
            OcppActions.MeterValues,
            new MeterValuesRequest(
                session.ConnectorId,
                session.TransactionId,
                [new MeterValueSample(SuiteClock.Instant, [malformed])]));

        var after = await GetSessionAsync(op.StationId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.ErrorCode, Is.EqualTo(expectedErrorCode));
            Assert.That(after.EnergyKwh, Is.EqualTo(before.EnergyKwh), "the refused reading changed no energy");
            Assert.That(after.IsOpen, Is.True, "the session is still running after the refusal");
        }
    }

    private static async Task<SessionResponse> GetSessionAsync(Guid stationId, int transactionId)
    {
        var sessions = await GetSessionsAsync(stationId);
        return sessions.Single(session => session.TransactionId == transactionId);
    }

    private static async Task<SessionResponse> GetSessionAsync(Guid stationId)
    {
        var sessions = await GetSessionsAsync(stationId);
        return sessions.Single();
    }

    private static async Task<List<SessionResponse>> GetSessionsAsync(Guid stationId)
    {
        using var response = await Proto.Context.Rest().GetAsync($"/api/stations/{stationId}/sessions");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        return response.ReadAsJson<List<SessionResponse>>()!;
    }

    private static async Task<List<ConnectorResponse>> GetConnectorsAsync(Guid stationId)
    {
        using var response = await Proto.Context.Rest().GetAsync($"/api/stations/{stationId}/connectors");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        return response.ReadAsJson<List<ConnectorResponse>>()!;
    }

    private static async Task<StationResponse> GetStationAsync(Guid stationId)
    {
        using var response = await Proto.Context.Rest().GetAsync($"/api/stations/{stationId}");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        return response.ReadAsJson<StationResponse>()!;
    }

    /// <summary>
    /// Waits for the invoice the worker issued for the session and reads it over REST, the same way
    /// the billing journeys do.
    /// </summary>
    private static async Task<RestResponse> ReadIssuedInvoiceAsync(SessionResponse session)
    {
        var message = await Proto.Context.Messaging().AwaitAsync(
            CsmsEvents.Exchange,
            candidate => CsmsMessages.IsInvoiceIssuedFor(candidate, session.Id),
            TimeSpan.FromSeconds(30));
        var issued = message.ReadRequired<InvoiceIssued>();
        return await Proto.Context.Rest().GetAsync($"/api/invoices/{issued.InvoiceId}");
    }
}
