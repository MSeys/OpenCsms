namespace OpenCsms.Application.Ports;

using OpenCsms.Domain;

/// <summary>The tariff writes a use case needs; implemented over the CSMS store.</summary>
public interface ITariffCommands
{
    /// <summary>Stores a new tariff and saves it.</summary>
    Task AddAsync(Tariff tariff, CancellationToken cancellationToken = default);
}
