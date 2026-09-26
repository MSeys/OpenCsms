namespace OpenCsms.Api.Ocpp;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// The charge points that are connected right now, keyed by their OCPP identity. The gateway adds a
/// connection while its socket lives; the operator-facing endpoints look one up to start a
/// server-initiated call. A charge point that reconnects replaces its old connection, which is closed.
/// </summary>
public sealed class ChargePointConnections
{
    private readonly ConcurrentDictionary<string, ChargePointConnection> _connections =
        new(StringComparer.Ordinal);

    public bool TryGet(string chargePointId, [NotNullWhen(true)] out ChargePointConnection? connection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chargePointId);
        return _connections.TryGetValue(chargePointId, out connection);
    }

    /// <summary>Registers a connection; whatever was connected before for this charge point is closed.</summary>
    public async Task RegisterAsync(ChargePointConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connections.TryGetValue(connection.ChargePointId, out var previous);
        _connections[connection.ChargePointId] = connection;
        if (previous is not null && !ReferenceEquals(previous, connection))
        {
            await previous.CloseAsync("another connection for the same charge point replaced this one");
        }
    }

    /// <summary>Removes a connection if it is still the registered one; a replaced connection removes nothing.</summary>
    public void Remove(ChargePointConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connections.TryRemove(new KeyValuePair<string, ChargePointConnection>(connection.ChargePointId, connection));
    }
}
