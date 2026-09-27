namespace OpenCsms.Application.Identity;

using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>
/// Registers a tenant and mints its machine credential. The raw key is returned once to the caller
/// that registered the tenant; the store receives only the hash, so the key cannot be recovered from
/// a later read.
/// </summary>
public sealed class TenantRegistration(ITenantCommands tenants, TimeProvider clock)
{
    public async Task<TenantRegistered> RegisterAsync(
        RegisterTenantCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var apiKey = MachineApiKey.Create();
        var tenant = Tenant.Create(command.Name ?? string.Empty, MachineApiKey.Hash(apiKey), clock.GetUtcNow());
        await tenants.AddAsync(tenant, cancellationToken);
        return new TenantRegistered(tenant, apiKey);
    }
}

/// <summary>The name a registration carries; the tenant id and the key are the product's to choose.</summary>
public sealed record RegisterTenantCommand(string? Name);

/// <summary>The stored tenant plus the one-time raw key the response shows.</summary>
public sealed record TenantRegistered(Tenant Tenant, string ApiKey);
