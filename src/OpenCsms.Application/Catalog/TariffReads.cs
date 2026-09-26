namespace OpenCsms.Application.Catalog;

using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>The tariff read surface: the tenants' tariffs in name order, filtered when a tenant is named.</summary>
public sealed class TariffReads(ITariffQueries tariffs)
{
    public Task<IReadOnlyList<Tariff>> ListAsync(string? tenantId, CancellationToken cancellationToken = default)
        => tariffs.ListAsync(tenantId, cancellationToken);
}
