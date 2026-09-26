namespace OpenCsms.Application.Ports;

using OpenCsms.Domain;

/// <summary>The station reads a use case needs; implemented over the CSMS store.</summary>
public interface IStationQueries
{
    /// <summary>Finds a station by id, or null when none is registered.</summary>
    Task<Station?> FindAsync(Guid stationId, CancellationToken cancellationToken = default);

    /// <summary>Finds the station whose OCPP identity is the charge point id, or null when none carries it.</summary>
    Task<Station?> FindByChargePointIdAsync(string chargePointId, CancellationToken cancellationToken = default);

    /// <summary>Finds a station by id within one tenant, or null when it is not that tenant's.</summary>
    Task<Station?> FindForTenantAsync(Guid stationId, string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Answers whether any station carries the id.</summary>
    Task<bool> ExistsAsync(Guid stationId, CancellationToken cancellationToken = default);

    /// <summary>Answers whether the tenant has a station with the id.</summary>
    Task<bool> ExistsForTenantAsync(Guid stationId, string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The stations in name order; <paramref name="tenantId"/> null lists every tenant's, which is
    /// the public status page's view.
    /// </summary>
    Task<IReadOnlyList<Station>> ListAsync(string? tenantId, CancellationToken cancellationToken = default);
}
