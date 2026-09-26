namespace OpenCsms.Domain;

/// <summary>
/// The error a connector reports alongside its status, in the product's own vocabulary. The protocol
/// layer maps the OCPP values to these and back, so the domain never carries a wire type.
/// </summary>
public enum ConnectorErrorCode
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
