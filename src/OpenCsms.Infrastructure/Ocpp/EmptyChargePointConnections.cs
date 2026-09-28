namespace OpenCsms.Infrastructure.Ocpp;

using System.Diagnostics.CodeAnalysis;
using OpenCsms.Application.Ports;

/// <summary>
/// The transport-less connection registry: no charge point is ever connected in a host that does not
/// serve the OCPP edge, so the application's operator commands answer "offline" instead of failing to
/// build. The API replaces this registration with the registry its gateway fills while sockets live.
/// </summary>
internal sealed class EmptyChargePointConnections : IChargePointConnections
{
    /// <inheritdoc />
    public bool TryGet(string chargePointId, [NotNullWhen(true)] out IChargePointConnection? connection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chargePointId);
        connection = null;
        return false;
    }
}
