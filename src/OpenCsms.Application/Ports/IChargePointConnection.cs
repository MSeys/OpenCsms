namespace OpenCsms.Application.Ports;

/// <summary>
/// One connected charge point, as a server-initiated command reaches it. The implementation owns the
/// socket and the wire: it translates the command into an OCPP call and the device's answer back into
/// this port's terms, so the use case never sees a payload.
/// </summary>
public interface IChargePointConnection
{
    /// <summary>
    /// Asks the charge point to start a transaction and returns its authorization decision. A device
    /// that refuses the call throws <see cref="ChargePointCallRefusedException"/>; a device that does
    /// not answer within <paramref name="timeout"/> throws <see cref="TimeoutException"/>; a
    /// connection that is lost while the call is in flight throws
    /// <see cref="ChargePointConnectionLostException"/>.
    /// </summary>
    Task<AuthorizationStatus> RemoteStartAsync(
        string idTag,
        int? connectorId,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    /// <summary>
    /// Asks the charge point to stop the named transaction and returns its authorization decision;
    /// the failure shapes match <see cref="RemoteStartAsync"/>.
    /// </summary>
    Task<AuthorizationStatus> RemoteStopAsync(
        int transactionId,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
