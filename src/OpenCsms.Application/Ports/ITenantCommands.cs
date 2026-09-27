namespace OpenCsms.Application.Ports;

using OpenCsms.Domain;

/// <summary>The tenant writes the registration path needs; implemented over the CSMS store.</summary>
public interface ITenantCommands
{
    /// <summary>Stores a newly registered tenant.</summary>
    Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default);
}
