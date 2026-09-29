namespace OpenCsms.Application.Ports;

using OpenCsms.Domain;

/// <summary>The charging-session writes a use case needs; implemented over the CSMS store.</summary>
public interface ISessionCommands
{
    /// <summary>Stores a newly started session and saves it.</summary>
    Task AddAsync(ChargingSession session, CancellationToken cancellationToken = default);

    /// <summary>Saves the changed session.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
