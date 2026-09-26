namespace OpenCsms.Application.Ports;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// The charge points that are connected right now. The gateway keeps the registry; the operator
/// commands ask it whether a call can reach the device at all before they send one, so a station
/// whose charge point is offline is a state the use case decides about, not a socket error.
/// </summary>
public interface IChargePointConnections
{
    /// <summary>Finds the connection for an OCPP identity; false means charge point is not connected.</summary>
    bool TryGet(string chargePointId, [NotNullWhen(true)] out IChargePointConnection? connection);
}
