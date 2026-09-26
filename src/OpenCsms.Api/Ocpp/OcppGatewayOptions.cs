namespace OpenCsms.Api.Ocpp;

/// <summary>
/// The OCPP gateway's settings. The heartbeat interval is what the CSMS asks a booting charge point to
/// use; the remote-call timeout bounds how long an operator action waits for the device's answer.
/// </summary>
public sealed class OcppGatewayOptions
{
    public const string SectionName = "Ocpp";

    /// <summary>Seconds between heartbeats the CSMS asks for, as in BootNotification.conf.</summary>
    public int HeartbeatIntervalSeconds { get; set; } = 300;

    /// <summary>How long a server-initiated call waits for the charge point's answer.</summary>
    public int RemoteCallTimeoutSeconds { get; set; } = 10;
}
