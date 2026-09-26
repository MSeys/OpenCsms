namespace OpenCsms.Application.Ports;

using OpenCsms.Domain;

/// <summary>The account reads a use case needs; implemented over the CSMS store.</summary>
public interface IUserQueries
{
    /// <summary>Finds the account for a normalized sign-in address, or null when none exists.</summary>
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Answers whether the sign-in address is already taken.</summary>
    Task<bool> ExistsAsync(string email, CancellationToken cancellationToken = default);
}
