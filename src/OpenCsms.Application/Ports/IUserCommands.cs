namespace OpenCsms.Application.Ports;

using OpenCsms.Domain;

/// <summary>The account writes a use case needs; implemented over the CSMS store.</summary>
public interface IUserCommands
{
    /// <summary>
    /// Stores a new account and saves it; false when the unique sign-in address was taken by another
    /// provisioning call in the meantime, so the caller can answer its own conflict shape.
    /// </summary>
    Task<bool> TryAddAsync(User user, CancellationToken cancellationToken = default);
}
