namespace OpenCsms.Application.Catalog;

using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>The station read surface: lookups, tenant-scoped existence checks and the name-ordered list.</summary>
public sealed class StationReads(IStationQueries stations)
{
    /// <summary>Finds a station by id, or null when none is registered.</summary>
    public Task<Station?> FindAsync(Guid stationId, CancellationToken cancellationToken = default)
        => stations.FindAsync(stationId, cancellationToken);

    /// <summary>Finds the station whose OCPP identity is the charge point id, or null when none carries it.</summary>
    public Task<Station?> FindByChargePointIdAsync(string chargePointId, CancellationToken cancellationToken = default)
        => stations.FindByChargePointIdAsync(chargePointId, cancellationToken);

    /// <summary>Finds a station by id within one tenant; another tenant's station looks like an unknown one.</summary>
    public Task<Station?> FindForTenantAsync(Guid stationId, string tenantId, CancellationToken cancellationToken = default)
        => stations.FindForTenantAsync(stationId, tenantId, cancellationToken);

    /// <summary>Answers whether any station carries the id.</summary>
    public Task<bool> ExistsAsync(Guid stationId, CancellationToken cancellationToken = default)
        => stations.ExistsAsync(stationId, cancellationToken);

    /// <summary>Answers whether the tenant has a station with the id.</summary>
    public Task<bool> ExistsForTenantAsync(Guid stationId, string tenantId, CancellationToken cancellationToken = default)
        => stations.ExistsForTenantAsync(stationId, tenantId, cancellationToken);

    /// <summary>The stations in name order; a null tenant lists every tenant's, the public page's view.</summary>
    public Task<IReadOnlyList<Station>> ListAsync(string? tenantId, CancellationToken cancellationToken = default)
        => stations.ListAsync(tenantId, cancellationToken);
}
