namespace OpenCsms.Application.Catalog;

using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>
/// Registers a tenant's tariff. The domain's factory is the validation, and its argument failures
/// travel to the caller for its own error shape; the store port persists the accepted tariff.
/// </summary>
public sealed class TariffRegistration(ITariffCommands tariffs)
{
    public async Task<Tariff> RegisterAsync(RegisterTariffCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tariff = Tariff.Create(
            command.TenantId,
            command.Name,
            command.EnergyPricePerKwh,
            command.StartFee,
            command.IdleFeePerHour,
            command.IdleGracePeriod,
            command.Currency ?? "EUR");
        await tariffs.AddAsync(tariff, cancellationToken);
        return tariff;
    }
}

/// <summary>The fields a tariff registration carries; the domain fills the defaults.</summary>
public sealed record RegisterTariffCommand(
    string TenantId,
    string Name,
    decimal EnergyPricePerKwh,
    decimal StartFee,
    decimal IdleFeePerHour,
    TimeSpan? IdleGracePeriod,
    string? Currency);
