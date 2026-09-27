namespace OpenCsms.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using OpenCsms.Domain;

/// <summary>The CSMS store. One context for the API and the billing worker, so both see one schema.</summary>
public sealed class CsmsDbContext(DbContextOptions<CsmsDbContext> options) : DbContext(options)
{
    public DbSet<Station> Stations => Set<Station>();

    public DbSet<Tariff> Tariffs => Set<Tariff>();

    public DbSet<ChargingSession> Sessions => Set<ChargingSession>();

    public DbSet<Connector> Connectors => Set<Connector>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<User> Users => Set<User>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Every mapping lives in its own IEntityTypeConfiguration beside the context; the domain
        // classes carry no persistence attributes.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CsmsDbContext).Assembly);
    }
}
