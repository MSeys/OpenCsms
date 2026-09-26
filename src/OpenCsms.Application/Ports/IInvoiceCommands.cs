namespace OpenCsms.Application.Ports;

using OpenCsms.Domain;

/// <summary>The invoice writes a use case needs; implemented over the CSMS store.</summary>
public interface IInvoiceCommands
{
    /// <summary>Stores a newly calculated invoice and saves it.</summary>
    Task AddAsync(Invoice invoice, CancellationToken cancellationToken = default);
}
