namespace OpenCsms.Api.Ocpp;

/// <summary>
/// The OCPP gateway's settings: the heartbeat interval is what the CSMS asks a booting charge point
/// to use. The operator commands' remote-call timeout is the application's own setting; both are read
/// from the same <c>Ocpp</c> section by the composition root.
/// </summary>
public sealed class OcppGatewayOptions
{
    public const string SectionName = "Ocpp";

    /// <summary>Seconds between heartbeats the CSMS asks for, as in BootNotification.conf.</summary>
    public int HeartbeatIntervalSeconds { get; set; } = 300;
}
