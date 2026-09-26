namespace OpenCsms.Application.Sessions;

using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>
/// Starts a session on a station connector: the station must exist, the connector must be one of its
/// own, and the session opens at the clock's instant. The store port persists it.
/// </summary>
public sealed class SessionStart(IStationQueries stations, ISessionCommands sessions, TimeProvider clock)
{
    public async Task<StartSessionOutcome> StartAsync(
        StartSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var station = await stations.FindAsync(command.StationId, cancellationToken);
        if (station is null)
        {
            return new SessionStationMissing(command.StationId);
        }

        if (command.ConnectorId < 1 || command.ConnectorId > station.ConnectorCount)
        {
            return new SessionConnectorOutsideStation(station, command.ConnectorId);
        }

        var session = ChargingSession.Start(station.TenantId, station.Id, command.ConnectorId, clock.GetUtcNow());
        await sessions.AddAsync(session, cancellationToken);
        return new SessionStarted(session);
    }
}

/// <summary>The connector a session starts on.</summary>
public sealed record StartSessionCommand(Guid StationId, int ConnectorId);

/// <summary>What starting a session ended in; the caller maps each to its own answer.</summary>
public abstract record StartSessionOutcome;

/// <summary>The session is stored.</summary>
public sealed record SessionStarted(ChargingSession Session) : StartSessionOutcome;

/// <summary>No station with that id is registered.</summary>
public sealed record SessionStationMissing(Guid StationId) : StartSessionOutcome;

/// <summary>The station exists but has no such connector; the caller explains the station's size.</summary>
public sealed record SessionConnectorOutsideStation(Station Station, int ConnectorId) : StartSessionOutcome;
