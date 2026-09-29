namespace OpenCsms.Api.Ocpp;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using OpenCsms.Application.Ports;

/// <summary>The live charge-point connections by OCPP identity; a reconnect replaces its entry.</summary>
public sealed class ChargePointConnections : IChargePointConnections
{
    private readonly ConcurrentDictionary<string, ChargePointConnection> _connections =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool TryGet(string chargePointId, [NotNullWhen(true)] out IChargePointConnection? connection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chargePointId);
        if (_connections.TryGetValue(chargePointId, out var found))
        {
            connection = found;
            return true;
        }

        connection = null;
        return false;
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
