namespace OpenCsms.Application.Catalog;

using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>
/// Registers a station against a tariff of the same tenant. The tariff checks are this use case's
/// rules - a missing tariff and a foreign one are distinct outcomes the caller answers in its own
/// validation shape - and the domain is the last word on the station itself.
/// </summary>
public sealed class StationRegistration(ITariffQueries tariffs, IStationCommands stations)
{
    public async Task<RegisterStationOutcome> RegisterAsync(
        RegisterStationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tariff = await tariffs.FindAsync(command.TariffId, cancellationToken);
        if (tariff is null)
        {
            return new StationTariffMissing(command.TariffId);
        }

        if (!string.Equals(tariff.TenantId, command.TenantId, StringComparison.Ordinal))
        {
            return new StationTariffOfAnotherTenant();
        }

        var station = Station.Register(
            command.TenantId,
            command.ChargePointId,
            command.Name,
            command.ConnectorCount,
            command.TariffId);
        await stations.AddAsync(station, cancellationToken);
        return new StationRegistered(station);
    }
}

/// <summary>The fields a station registration carries.</summary>
public sealed record RegisterStationCommand(
    string TenantId,
    string ChargePointId,
    string Name,
    int ConnectorCount,
    Guid TariffId);

/// <summary>What a station registration ended in; the caller maps each to its own answer.</summary>
public abstract record RegisterStationOutcome;

/// <summary>The station is stored.</summary>
public sealed record StationRegistered(Station Station) : RegisterStationOutcome;

/// <summary>No tariff with that id is registered.</summary>
public sealed record StationTariffMissing(Guid TariffId) : RegisterStationOutcome;

/// <summary>The tariff is registered but belongs to another tenant.</summary>
public sealed record StationTariffOfAnotherTenant : RegisterStationOutcome;
