namespace OpenCsms.Infrastructure;

using OpenCsms.Infrastructure.Persistence;

/// <summary>
/// The store's readiness probe. The API's health endpoint asks this instead of the context itself,
/// so the composition root never reaches into EF to answer whether the database is up.
/// </summary>
public sealed class CsmsStoreProbe(CsmsDbContext db)
{
    /// <summary>Answers whether the configured database accepts a connection.</summary>
    public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
        => db.Database.CanConnectAsync(cancellationToken);
}
