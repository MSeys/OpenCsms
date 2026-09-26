namespace OpenCsms.Suite.Devices;

using System.Globalization;
using OpenCsms.Protocol.Ocpp;
using ProtoTest.Core;
using ProtoTest.Devices;

/// <summary>
/// The OCPP 1.6J charge point the suite drives through ProtoTest.Devices.WebSocket, against the CSMS's
/// documented subset. Calls use the shared framing, every client call waits for its own answer, and a
/// server-initiated call (RemoteStart/Stop) can be received and answered - the duplex a real charger
/// has. <see cref="RefusedAsync"/> reads the call error the gateway answers a bad call with; a server
/// call can also be answered with a call error (<see cref="RefuseRemoteStartAsync"/>), the way a device
/// that cannot execute it refuses. <see cref="PlugInAsync"/>, <see cref="MeterValuesAsync"/> and
/// <see cref="UnplugAsync"/> are the ergonomic face a journey reads: they send the connector's own meter
/// readings and run its status transitions, and their timestamps come from the test's clock, so advanced
/// time is what the wire carries.
/// </summary>
public sealed class AcCharger : ProtoDevice
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);
    private readonly Dictionary<int, AcChargerSession> _sessions = [];
    private readonly Dictionary<int, int> _meterWh = [];
    private int _nextMessageId;

    /// <summary>Boots the charge point and returns the CSMS's registration answer.</summary>
    public Task<BootNotificationResponse> BootAsync(
        string vendor = "OpenCSMS",
        string model = "Sim-1",
        CancellationToken cancellationToken = default)
        => CallForResultAsync<BootNotificationResponse>(
            OcppActions.BootNotification,
            new BootNotificationRequest(vendor, model),
            "the CSMS answers the boot notification",
            cancellationToken);

    /// <summary>Sends a heartbeat and returns the CSMS's current time.</summary>
    public Task<HeartbeatResponse> HeartbeatAsync(CancellationToken cancellationToken = default)
        => CallForResultAsync<HeartbeatResponse>(
            OcppActions.Heartbeat,
            new HeartbeatRequest(),
            "the CSMS answers the heartbeat",
            cancellationToken);

    /// <summary>Reports one connector's status.</summary>
    public Task ReportStatusAsync(
        int connectorId,
        ConnectorStatus status,
        ChargePointErrorCode errorCode = ChargePointErrorCode.NoError,
        DateTimeOffset? timestamp = null,
        CancellationToken cancellationToken = default)
        => CallForResultAsync<StatusNotificationResponse>(
            OcppActions.StatusNotification,
            new StatusNotificationRequest(connectorId, status, errorCode, Timestamp: timestamp ?? DateTimeOffset.UtcNow),
            "the CSMS records the connector status",
            cancellationToken);

    /// <summary>Starts a transaction; the meter reading is in watt-hours, as OCPP frames it.</summary>
    public Task<StartTransactionResponse> StartTransactionAsync(
        int connectorId,
        string idTag,
        int meterStartWh = 0,
        DateTimeOffset? timestamp = null,
        CancellationToken cancellationToken = default)
        => CallForResultAsync<StartTransactionResponse>(
            OcppActions.StartTransaction,
            new StartTransactionRequest(connectorId, idTag, meterStartWh, timestamp ?? DateTimeOffset.UtcNow),
            "the CSMS starts a transaction",
            cancellationToken);

    /// <summary>Sends one cumulative energy reading for the transaction.</summary>
    public Task MeterValuesAsync(
        int connectorId,
        int transactionId,
        decimal energyKwh,
        DateTimeOffset? timestamp = null,
        CancellationToken cancellationToken = default)
        => CallForResultAsync<MeterValuesResponse>(
            OcppActions.MeterValues,
            new MeterValuesRequest(
                connectorId,
                transactionId,
                [EnergySample(energyKwh, timestamp ?? DateTimeOffset.UtcNow)]),
            "the CSMS records the meter values",
            cancellationToken);

    /// <summary>Stops the transaction with the final cumulative reading.</summary>
    public Task<StopTransactionResponse> StopTransactionAsync(
        int transactionId,
        decimal energyKwh,
        string? reason = null,
        DateTimeOffset? timestamp = null,
        CancellationToken cancellationToken = default)
        => CallForResultAsync<StopTransactionResponse>(
            OcppActions.StopTransaction,
            new StopTransactionRequest(
                transactionId,
                (int)(energyKwh * 1000m),
                timestamp ?? DateTimeOffset.UtcNow,
                Reason: reason),
            "the CSMS ends the transaction",
            cancellationToken);

    /// <summary>
    /// Sends one MeterValues call with samples the caller shapes - the malformed cases a real charge
    /// point can send - and returns the CSMS's answer, a result or the call error that refused it.
    /// </summary>
    public Task<OcppMessage> MeterValuesAsync(
        int connectorId,
        int? transactionId,
        IReadOnlyList<MeterValueSample> meterValues,
        CancellationToken cancellationToken = default)
        => CallAsync(
            OcppActions.MeterValues,
            new MeterValuesRequest(connectorId, transactionId, meterValues),
            cancellationToken);

    /// <summary>
    /// Sends a call and returns the CSMS's answer exactly as it arrived - a call result, or the call
    /// error the gateway refused it with. The typed calls above read a result; this is the surface the
    /// error-path tests read a refusal with.
    /// </summary>
    public Task<OcppMessage> CallAsync(
        string action,
        object payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(payload);
        return ExchangeAsync(action, payload, $"the CSMS answers {action}", cancellationToken);
    }

    /// <summary>
    /// Sends a call the CSMS must refuse and returns the call error it answered with; a call the CSMS
    /// answers with a result fails the test naming that result.
    /// </summary>
    public async Task<OcppCallError> RefusedAsync(
        string action,
        object payload,
        CancellationToken cancellationToken = default)
        => await CallAsync(action, payload, cancellationToken) as OcppCallError
            ?? throw new InvalidOperationException(
                $"The CSMS answered {action} with a call result; the test expected a call error.");

    /// <summary>
    /// Plugs in on a connector the way a charge point does: the connector reports Charging, then
    /// StartTransaction carries the meter register the connector currently stands at. The returned
    /// session is what <see cref="MeterValuesAsync"/> and <see cref="UnplugAsync"/> address.
    /// </summary>
    public async Task<AcChargerSession> PlugInAsync(
        string rfid,
        int connectorId = 1,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rfid);
        if (_sessions.ContainsKey(connectorId))
        {
            throw new InvalidOperationException(
                $"Connector {connectorId} already has a running transaction; unplug it before plugging in again.");
        }

        var at = Now();
        await ReportStatusAsync(connectorId, ConnectorStatus.Charging, timestamp: at, cancellationToken: cancellationToken);
        var meterStartWh = _meterWh.GetValueOrDefault(connectorId);
        var response = await StartTransactionAsync(connectorId, rfid, meterStartWh, at, cancellationToken);
        if (response.IdTagInfo.Status != OcppAuthorizationStatus.Accepted)
        {
            throw new InvalidOperationException(
                $"The CSMS answered StartTransaction on connector {connectorId} with '{response.IdTagInfo.Status}'; no transaction is running.");
        }

        var session = new AcChargerSession(connectorId, rfid, response.TransactionId, meterStartWh);
        _sessions[connectorId] = session;
        return session;
    }

    /// <summary>
    /// Sends a meter reading for the plugged-in connector: <paramref name="energyKwh"/> is the
    /// session's cumulative energy so far, and the wire carries the connector's meter register - its
    /// reading at plug-in plus this session's energy, as a real meter counts.
    /// </summary>
    public async Task MeterValuesAsync(
        decimal energyKwh,
        int connectorId = 1,
        CancellationToken cancellationToken = default)
    {
        var session = RequireRunning(connectorId);
        _meterWh[connectorId] = session.MeterStartWh + ToWh(energyKwh);
        await MeterValuesAsync(connectorId, session.TransactionId, _meterWh[connectorId] / 1000m, Now(), cancellationToken);
    }

    /// <summary>
    /// Unplugs the connector: StopTransaction carries the meter stop, the connector reports Finishing
    /// and then Available, and the session is no longer running.
    /// </summary>
    public async Task UnplugAsync(
        int connectorId = 1,
        string reason = "Local",
        CancellationToken cancellationToken = default)
    {
        var session = RequireRunning(connectorId);
        var at = Now();
        await StopTransactionAsync(
            session.TransactionId,
            _meterWh.GetValueOrDefault(connectorId, session.MeterStartWh) / 1000m,
            reason,
            at,
            cancellationToken);
        await ReportStatusAsync(connectorId, ConnectorStatus.Finishing, timestamp: at, cancellationToken: cancellationToken);
        await ReportStatusAsync(connectorId, ConnectorStatus.Available, timestamp: at, cancellationToken: cancellationToken);
        _sessions.Remove(connectorId);
    }

    /// <summary>
    /// Waits for the CSMS's RemoteStartTransaction, answers it with the given authorization status,
    /// and returns the request the test observes - the whole server-initiated step in one call.
    /// </summary>
    public async Task<RemoteStartTransactionRequest> AnswerRemoteStartAsync(
        OcppAuthorizationStatus status = OcppAuthorizationStatus.Accepted,
        CancellationToken cancellationToken = default)
    {
        var call = await ReceiveCallAsync(
            OcppActions.RemoteStartTransaction,
            "the CSMS starts a transaction remotely",
            cancellationToken);
        var request = OcppJson.ReadPayload<RemoteStartTransactionRequest>(call.Payload);
        await AnswerAsync(call.MessageId, new RemoteStartTransactionResponse(new IdTagInfo(status)), cancellationToken);
        return request;
    }

    /// <summary>
    /// Waits for the CSMS's RemoteStopTransaction, answers it with the given authorization status, and
    /// returns the request the test observes.
    /// </summary>
    public async Task<RemoteStopTransactionRequest> AnswerRemoteStopAsync(
        OcppAuthorizationStatus status = OcppAuthorizationStatus.Accepted,
        CancellationToken cancellationToken = default)
    {
        var call = await ReceiveCallAsync(
            OcppActions.RemoteStopTransaction,
            "the CSMS stops a transaction remotely",
            cancellationToken);
        var request = OcppJson.ReadPayload<RemoteStopTransactionRequest>(call.Payload);
        await AnswerAsync(call.MessageId, new RemoteStopTransactionResponse(new IdTagInfo(status)), cancellationToken);
        return request;
    }

    /// <summary>
    /// Waits for the CSMS's RemoteStartTransaction and refuses it with an OCPP call error instead of an
    /// answer - the device-level refusal the operator's REST call maps to 502 - and returns the request
    /// the test observes.
    /// </summary>
    public Task<RemoteStartTransactionRequest> RefuseRemoteStartAsync(
        string errorCode = OcppErrorCodes.GenericError,
        string description = "the charge point refused the remote start",
        CancellationToken cancellationToken = default)
        => RefuseCallAsync<RemoteStartTransactionRequest>(
            OcppActions.RemoteStartTransaction,
            "the CSMS starts a transaction remotely",
            errorCode,
            description,
            cancellationToken);

    /// <summary>
    /// Waits for the CSMS's RemoteStopTransaction and refuses it with an OCPP call error instead of an
    /// answer, returning the request the test observes.
    /// </summary>
    public Task<RemoteStopTransactionRequest> RefuseRemoteStopAsync(
        string errorCode = OcppErrorCodes.GenericError,
        string description = "the charge point refused the remote stop",
        CancellationToken cancellationToken = default)
        => RefuseCallAsync<RemoteStopTransactionRequest>(
            OcppActions.RemoteStopTransaction,
            "the CSMS stops a transaction remotely",
            errorCode,
            description,
            cancellationToken);

    /// <summary>Waits for a call the CSMS starts, for example a remote start or stop.</summary>
    public async Task<OcppCall> ReceiveCallAsync(
        string action,
        string description,
        CancellationToken cancellationToken = default)
    {
        var frame = await ExpectAsync(
            description,
            candidate => IsCallFor(candidate, action),
            CallTimeout,
            cancellationToken);
        return (OcppCall)OcppJson.Parse(frame.AsText());
    }

    /// <summary>Answers a call the CSMS started with its call result payload.</summary>
    public ValueTask AnswerAsync(string messageId, object payload, CancellationToken cancellationToken = default)
        => SendTextAsync(OcppJson.SerializeCallResult(messageId, payload), cancellationToken);

    /// <summary>Answers a call the CSMS started with an OCPP call error, the way a device refuses a call.</summary>
    public ValueTask AnswerErrorAsync(
        string messageId,
        string errorCode,
        string description,
        CancellationToken cancellationToken = default)
        => SendTextAsync(OcppJson.SerializeCallError(messageId, errorCode, description), cancellationToken);

    /// <summary>The refusal half of a server-initiated call: read the request, answer an error, return it.</summary>
    private async Task<TRequest> RefuseCallAsync<TRequest>(
        string action,
        string description,
        string errorCode,
        string errorDescription,
        CancellationToken cancellationToken)
    {
        var call = await ReceiveCallAsync(action, description, cancellationToken);
        var request = OcppJson.ReadPayload<TRequest>(call.Payload);
        await AnswerErrorAsync(call.MessageId, errorCode, errorDescription, cancellationToken);
        return request;
    }

    private async Task<T> CallForResultAsync<T>(
        string action,
        object payload,
        string description,
        CancellationToken cancellationToken)
    {
        var answer = await ExchangeAsync(action, payload, description, cancellationToken);
        return answer switch
        {
            OcppCallResult result => OcppJson.ReadPayload<T>(result.Payload),
            OcppCallError error => throw new InvalidOperationException(
                $"The CSMS refused {action}: {error.ErrorCode} — {error.Description}"),
            _ => throw new OcppProtocolException($"The answer to {action} was not a result or an error.")
        };
    }

    private async Task<OcppMessage> ExchangeAsync(
        string action,
        object payload,
        string description,
        CancellationToken cancellationToken)
    {
        var messageId = Interlocked.Increment(ref _nextMessageId).ToString(CultureInfo.InvariantCulture);
        await SendTextAsync(OcppJson.SerializeCall(messageId, action, payload), cancellationToken);
        var frame = await ExpectAsync(
            description,
            candidate => IsAnswerFor(candidate, messageId),
            CallTimeout,
            cancellationToken);
        return OcppJson.Parse(frame.AsText());
    }

    private static MeterValueSample EnergySample(decimal energyKwh, DateTimeOffset timestamp)
        => new(
            timestamp,
            [
                new SampledValue(
                    (energyKwh * 1000m).ToString("0.###", CultureInfo.InvariantCulture),
                    Measurand: OcppEnergy.ActiveImportRegister,
                    Unit: "Wh")
            ]);

    private static int ToWh(decimal energyKwh) => (int)(energyKwh * 1000m);

    // The simulator's own clock is the test's clock: a journey advances time and the frames it sends
    // carry the advanced instant, exactly like the gateway's answers.
    private static DateTimeOffset Now() => Proto.Context.Clock.GetUtcNow();

    private AcChargerSession RequireRunning(int connectorId)
        => _sessions.TryGetValue(connectorId, out var session)
            ? session
            : throw new InvalidOperationException(
                $"Connector {connectorId} has no running transaction; plug in before reporting meter values or unplugging.");

    private static bool IsAnswerFor(DeviceFrame frame, string messageId)
        => frame.TryGetText(out var text)
            && OcppJson.TryParse(text, out var message)
            && message is OcppCallResult or OcppCallError
            && string.Equals(message.MessageId, messageId, StringComparison.Ordinal);

    private static bool IsCallFor(DeviceFrame frame, string action)
        => frame.TryGetText(out var text)
            && OcppJson.TryParse(text, out var message)
            && message is OcppCall call
            && string.Equals(call.Action, action, StringComparison.Ordinal);
}
