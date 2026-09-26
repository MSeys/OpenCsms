namespace OpenCsms.Application.Ports;

/// <summary>
/// A charge point refused a server-initiated call with an OCPP call error. The operator commands map
/// it to the caller's own failure shape; the code is the device's answer, not the CSMS's.
/// </summary>
public sealed class ChargePointCallRefusedException(string errorCode, string description) : Exception(description)
{
    /// <summary>The OCPP error code the charge point answered with.</summary>
    public string ErrorCode { get; } = errorCode;
}
