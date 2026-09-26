namespace OpenCsms.Application.Ports;

using OpenCsms.Domain;

/// <summary>The connector writes a use case needs; implemented over the CSMS store.</summary>
public interface IConnectorCommands
{
    /// <summary>Stores a newly reported connector and saves it.</summary>
    Task AddAsync(Connector connector, CancellationToken cancellationToken = default);

    /// <summary>Saves the state of a connector the use case just changed (a status notification).</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
