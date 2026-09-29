namespace OpenCsms.Application.Ports;

using OpenCsms.Domain;

/// <summary>The station writes a use case needs; implemented over the CSMS store.</summary>
public interface IStationCommands
{
    /// <summary>Stores a newly registered station and saves it.</summary>
    Task AddAsync(Station station, CancellationToken cancellationToken = default);

    /// <summary>Saves the changed station.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
