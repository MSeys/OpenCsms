namespace OpenCsms.Protocol.Ocpp;

using OpenCsms.Application.Ports;
using Domain = OpenCsms.Domain;

/// <summary>
/// Translates between the OCPP wire vocabulary and the product's own: the domain's connector status
/// and connector error code, and the application's authorization decision. The wire types never leave
/// this project, so the mapping lives beside them.
/// </summary>
public static class OcppTranslator
{
    /// <summary>Reads an OCPP connector status as the domain's own.</summary>
    public static Domain.ConnectorStatus ToDomain(ConnectorStatus status) => status switch
    {
        ConnectorStatus.Available => Domain.ConnectorStatus.Available,
        ConnectorStatus.Preparing => Domain.ConnectorStatus.Preparing,
        ConnectorStatus.Charging => Domain.ConnectorStatus.Charging,
        ConnectorStatus.SuspendedEVSE => Domain.ConnectorStatus.SuspendedEVSE,
        ConnectorStatus.SuspendedEV => Domain.ConnectorStatus.SuspendedEV,
        ConnectorStatus.Finishing => Domain.ConnectorStatus.Finishing,
        ConnectorStatus.Reserved => Domain.ConnectorStatus.Reserved,
        ConnectorStatus.Unavailable => Domain.ConnectorStatus.Unavailable,
        ConnectorStatus.Faulted => Domain.ConnectorStatus.Faulted,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "The OCPP connector status is outside OCPP 1.6J.")
    };

    /// <summary>Reads an OCPP connector error code as the domain's own.</summary>
    public static Domain.ConnectorErrorCode ToDomain(ChargePointErrorCode errorCode) => errorCode switch
    {
        ChargePointErrorCode.NoError => Domain.ConnectorErrorCode.NoError,
        ChargePointErrorCode.ConnectorLockFailure => Domain.ConnectorErrorCode.ConnectorLockFailure,
        ChargePointErrorCode.EVCommunicationError => Domain.ConnectorErrorCode.EVCommunicationError,
        ChargePointErrorCode.GroundFailure => Domain.ConnectorErrorCode.GroundFailure,
        ChargePointErrorCode.HighTemperature => Domain.ConnectorErrorCode.HighTemperature,
        ChargePointErrorCode.InternalError => Domain.ConnectorErrorCode.InternalError,
        ChargePointErrorCode.LocalListConflict => Domain.ConnectorErrorCode.LocalListConflict,
        ChargePointErrorCode.OverCurrentFailure => Domain.ConnectorErrorCode.OverCurrentFailure,
        ChargePointErrorCode.OverVoltage => Domain.ConnectorErrorCode.OverVoltage,
        ChargePointErrorCode.PowerMeterFailure => Domain.ConnectorErrorCode.PowerMeterFailure,
        ChargePointErrorCode.PowerSwitchFailure => Domain.ConnectorErrorCode.PowerSwitchFailure,
        ChargePointErrorCode.ReaderFailure => Domain.ConnectorErrorCode.ReaderFailure,
        ChargePointErrorCode.ResetFailure => Domain.ConnectorErrorCode.ResetFailure,
        ChargePointErrorCode.UnderVoltage => Domain.ConnectorErrorCode.UnderVoltage,
        ChargePointErrorCode.WeakSignal => Domain.ConnectorErrorCode.WeakSignal,
        _ => throw new ArgumentOutOfRangeException(nameof(errorCode), errorCode, "The OCPP error code is outside OCPP 1.6J.")
    };

    /// <summary>Reads the charge point's OCPP authorization decision as the application's own.</summary>
    public static AuthorizationStatus ToAuthorization(OcppAuthorizationStatus status) => status switch
    {
        OcppAuthorizationStatus.Accepted => AuthorizationStatus.Accepted,
        OcppAuthorizationStatus.Blocked => AuthorizationStatus.Blocked,
        OcppAuthorizationStatus.Expired => AuthorizationStatus.Expired,
        OcppAuthorizationStatus.Invalid => AuthorizationStatus.Invalid,
        OcppAuthorizationStatus.ConcurrentTx => AuthorizationStatus.ConcurrentTx,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "The OCPP authorization status is outside OCPP 1.6J.")
    };
}
