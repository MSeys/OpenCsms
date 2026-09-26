namespace OpenCsms.Application.Ports;

/// <summary>
/// The charge point's authorization decision for a transaction, in the application's terms. A
/// device's answer to a server-initiated command carries one of these; the transport adapter
/// translates the wire value, so the use case never sees the OCPP enum.
/// </summary>
public enum AuthorizationStatus
{
    Accepted,
    Blocked,
    Expired,
    Invalid,

    /// <summary>Another transaction for the same id tag or connector is already running.</summary>
    ConcurrentTx
}
