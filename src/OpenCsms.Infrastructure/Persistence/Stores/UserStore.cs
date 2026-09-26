namespace OpenCsms.Infrastructure.Persistence.Stores;

using Microsoft.EntityFrameworkCore;
using OpenCsms.Application.Ports;
using OpenCsms.Domain;

/// <summary>The account queries and commands, over the one CSMS store.</summary>
public sealed class UserStore(CsmsDbContext db) : IUserQueries, IUserCommands
{
    /// <inheritdoc />
    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
        => db.Users.FirstOrDefaultAsync(user => user.Email == email, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string email, CancellationToken cancellationToken = default)
        => db.Users.AnyAsync(candidate => candidate.Email == email, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryAddAsync(User user, CancellationToken cancellationToken = default)
    {
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Two provisioning calls raced over the same address; the unique index is the truth.
            return false;
        }
    }
}
