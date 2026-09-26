namespace OpenCsms.Application.Ports;

using OpenCsms.Domain;

/// <summary>The tariff reads a use case needs; implemented over the CSMS store.</summary>
public interface ITariffQueries
{
    /// <summary>Finds a tariff by id, or null when none is registered.</summary>
    Task<Tariff?> FindAsync(Guid tariffId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The tariffs in name order; <paramref name="tenantId"/> null lists every tenant's, which is the
    /// machine API's filter.
    /// </summary>
    Task<IReadOnlyList<Tariff>> ListAsync(string? tenantId, CancellationToken cancellationToken = default);
}
