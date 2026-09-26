namespace OpenCsms.Suite.Devices;

using OpenCsms.Protocol.Ocpp;
using ProtoTest.Devices;

/// <summary>
/// The message kinds of the CSMS's OCPP 1.6J subset, so the trace names each frame's action and the
/// coverage report can be read against the documented subset. A result or error frame carries no action
/// name, so it classifies as the frame type it is.
/// </summary>
public sealed class OcppProtocol : IProtoDeviceProtocol
{
    public string Name => "OCPP 1.6J";

    public IReadOnlyList<DeviceProtocolEntry> Entries { get; } =
    [
        new(OcppActions.BootNotification, "the charge point introduces itself"),
        new(OcppActions.Heartbeat, "the charge point is alive"),
        new(OcppActions.StatusNotification, "a connector changed state"),
        new(OcppActions.StartTransaction, "the id tag plugs in"),
        new(OcppActions.MeterValues, "cumulative meter readings"),
        new(OcppActions.StopTransaction, "the transaction ends"),
        new(OcppActions.RemoteStartTransaction, "the CSMS starts a transaction"),
        new(OcppActions.RemoteStopTransaction, "the CSMS stops a transaction"),
        new("CallResult", "the answer to a call"),
        new("CallError", "a call the receiver refused")
    ];

    public string? Classify(DeviceFrame frame)
    {
        if (!frame.TryGetText(out var text) || !OcppJson.TryParse(text, out var message))
        {
            return null;
        }

        return message switch
        {
            OcppCall call => call.Action,
            OcppCallResult => "CallResult",
            OcppCallError => "CallError",
            _ => null
        };
    }
}
