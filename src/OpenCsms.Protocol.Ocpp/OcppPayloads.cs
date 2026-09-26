namespace OpenCsms.Protocol.Ocpp;

using System.Text.Json.Serialization;

/// <summary>The registration states a CSMS can answer a boot notification with.</summary>
public enum OcppRegistrationStatus
{
    /// <summary>The charge point is known and may operate.</summary>
    Accepted,

    /// <summary>The CSMS needs more time before it can decide; the charge point retries.</summary>
    Pending,

    /// <summary>The charge point is not registered with this CSMS.</summary>
    Rejected
}

/// <summary>How the CSMS answers an id tag used to start or stop a transaction.</summary>
public enum OcppAuthorizationStatus
{
    Accepted,
    Blocked,
    Expired,
    Invalid,

    /// <summary>Another transaction for the same id tag or connector is already running.</summary>
    ConcurrentTx
}

/// <summary>The connector states OCPP 1.6J defines; connector 0 is the charge point itself.</summary>
public enum ConnectorStatus
{
    Available,
    Preparing,
    Charging,
    SuspendedEVSE,
    SuspendedEV,
    Finishing,
    Reserved,
    Unavailable,
    Faulted
}

/// <summary>The error codes a charge point reports alongside its status.</summary>
public enum ChargePointErrorCode
{
    NoError,
    ConnectorLockFailure,
    EVCommunicationError,
    GroundFailure,
    HighTemperature,
    InternalError,
    LocalListConflict,
    OverCurrentFailure,
    OverVoltage,
    PowerMeterFailure,
    PowerSwitchFailure,
    ReaderFailure,
    ResetFailure,
    UnderVoltage,
    WeakSignal
}

/// <summary>The authorization decision the CSMS returns with StartTransaction and remote commands.</summary>
public sealed record IdTagInfo(
    OcppAuthorizationStatus Status,
    DateTimeOffset? ExpiryDate = null,
    string? ParentIdTag = null);

/// <summary>BootNotification.req: the charge point introduces itself and its firmware.</summary>
public sealed record BootNotificationRequest(
    string ChargePointVendor,
    string ChargePointModel,
    string? ChargePointSerialNumber = null,
    string? ChargePointFirmwareVersion = null,
    string? Iccid = null,
    string? Imsi = null,
    string? MeterType = null,
    string? MeterSerialNumber = null);

/// <summary>BootNotification.conf: whether the CSMS accepts the charge point and when to heartbeat.</summary>
public sealed record BootNotificationResponse(
    OcppRegistrationStatus Status,
    DateTimeOffset CurrentTime,
    int Interval);

/// <summary>Heartbeat.req: no payload.</summary>
public sealed record HeartbeatRequest;

/// <summary>Heartbeat.conf: the CSMS's current time, so the charge point can correct its clock.</summary>
public sealed record HeartbeatResponse(DateTimeOffset CurrentTime);

/// <summary>StatusNotification.req: one connector's state change.</summary>
public sealed record StatusNotificationRequest(
    int ConnectorId,
    ConnectorStatus Status,
    ChargePointErrorCode ErrorCode,
    string? Info = null,
    DateTimeOffset? Timestamp = null,
    string? VendorId = null,
    string? VendorErrorCode = null);

/// <summary>StatusNotification.conf: no payload.</summary>
public sealed record StatusNotificationResponse;

/// <summary>StartTransaction.req: the id tag plugs in; the meter reading is in watt-hours.</summary>
public sealed record StartTransactionRequest(
    int ConnectorId,
    string IdTag,
    int MeterStart,
    DateTimeOffset Timestamp,
    int? ReservationId = null);

/// <summary>StartTransaction.conf: the CSMS-assigned transaction number and the authorization decision.</summary>
public sealed record StartTransactionResponse(
    int TransactionId,
    IdTagInfo IdTagInfo);

/// <summary>One sample of a meter, at the moment the charge point recorded it.</summary>
public sealed record SampledValue(
    string Value,
    string? Context = null,
    string? Format = null,
    string? Measurand = null,
    string? Phase = null,
    string? Location = null,
    string? Unit = null);

/// <summary>One timestamped batch of samples in a MeterValues or StopTransaction frame.</summary>
public sealed record MeterValueSample(
    DateTimeOffset Timestamp,
    IReadOnlyList<SampledValue> SampledValue);

/// <summary>MeterValues.req: a batch of readings for the given transaction.</summary>
public sealed record MeterValuesRequest(
    int ConnectorId,
    int? TransactionId,
    [property: JsonPropertyName("meterValue")] IReadOnlyList<MeterValueSample> MeterValue);

/// <summary>MeterValues.conf: no payload.</summary>
public sealed record MeterValuesResponse;

/// <summary>StopTransaction.req: the end meter reading in watt-hours and why the transaction stopped.</summary>
public sealed record StopTransactionRequest(
    int? TransactionId,
    int MeterStop,
    DateTimeOffset Timestamp,
    string? IdTag = null,
    string? Reason = null,
    IReadOnlyList<MeterValueSample>? TransactionData = null);

/// <summary>StopTransaction.conf: the optional authorization decision for the id tag that stopped.</summary>
public sealed record StopTransactionResponse(IdTagInfo? IdTagInfo = null);

/// <summary>RemoteStartTransaction.req: the CSMS asks the charge point to start a transaction.</summary>
public sealed record RemoteStartTransactionRequest(
    string IdTag,
    int? ConnectorId = null);

/// <summary>RemoteStartTransaction.conf: the charge point's authorization decision.</summary>
public sealed record RemoteStartTransactionResponse(IdTagInfo IdTagInfo);

/// <summary>RemoteStopTransaction.req: the CSMS asks the charge point to stop a transaction.</summary>
public sealed record RemoteStopTransactionRequest(int TransactionId);

/// <summary>RemoteStopTransaction.conf: the charge point's decision.</summary>
public sealed record RemoteStopTransactionResponse(IdTagInfo IdTagInfo);
