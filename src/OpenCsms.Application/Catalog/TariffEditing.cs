namespace OpenCsms.Application.Catalog;

using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>
/// Reprices a tenant's tariff. The tariff must belong to the calling tenant - another tenant's
/// tariff answers the same outcome as a missing one, so the caller cannot tell them apart - and
/// the domain's pricing rules are the last word on the new values. Stored invoices keep the prices
/// they were billed at; only sessions billed afterwards see the new ones.
/// </summary>
public sealed class TariffEditing(ITariffQueries tariffs, ITariffCommands commands)
{
    public async Task<UpdateTariffOutcome> UpdateAsync(
        Guid tariffId,
        string tenantId,
        UpdateTariffCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tariff = await tariffs.FindForTenantAsync(tariffId, tenantId, cancellationToken);
        if (tariff is null)
        {
            return new TariffUpdateMissing(tariffId);
        }

        tariff.UpdatePricing(
            command.EnergyPricePerKwh,
            command.StartFee,
            command.IdleFeePerHour,
            command.IdleGracePeriod);
        await commands.SaveAsync(cancellationToken);
        return new TariffUpdated(tariff);
    }
}

/// <summary>The prices a tariff repricing carries; the domain fills no defaults here.</summary>
public sealed record UpdateTariffCommand(
    decimal EnergyPricePerKwh,
    decimal StartFee,
    decimal IdleFeePerHour,
    TimeSpan IdleGracePeriod);

/// <summary>What a tariff repricing ended in; the caller maps each to its own answer.</summary>
public abstract record UpdateTariffOutcome;

/// <summary>The tariff is repriced and stored.</summary>
public sealed record TariffUpdated(Tariff Tariff) : UpdateTariffOutcome;

/// <summary>No tariff with that id belongs to the tenant.</summary>
public sealed record TariffUpdateMissing(Guid TariffId) : UpdateTariffOutcome;
