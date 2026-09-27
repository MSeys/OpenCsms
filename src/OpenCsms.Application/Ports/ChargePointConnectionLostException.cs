namespace OpenCsms.Application.Ports;

/// <summary>
/// A server-initiated call could not travel its connection: the charge point dropped the socket or a
/// reconnect replaced it while the call was in flight. The connection's fate is unknown, so the
/// operator commands answer the charge point is offline instead of letting a transport failure escape.
/// </summary>
public sealed class ChargePointConnectionLostException(string chargePointId, Exception? innerException = null)
    : Exception($"The connection to charge point '{chargePointId}' was lost.", innerException)
{
    /// <summary>The charge point identity whose connection was lost.</summary>
    public string ChargePointId { get; } = chargePointId;
}
