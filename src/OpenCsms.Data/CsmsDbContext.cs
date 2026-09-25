namespace OpenCsms.Data;

using Microsoft.EntityFrameworkCore;
using OpenCsms.Domain;

/// <summary>The CSMS store. One context for the API and the billing worker, so both see one schema.</summary>
public sealed class CsmsDbContext(DbContextOptions<CsmsDbContext> options) : DbContext(options)
{
    public DbSet<Station> Stations => Set<Station>();

    public DbSet<Tariff> Tariffs => Set<Tariff>();

    public DbSet<ChargingSession> Sessions => Set<ChargingSession>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tariff>(tariff =>
        {
            tariff.Property(value => value.Name).HasMaxLength(120);
            tariff.Property(value => value.Currency).HasMaxLength(3);
            tariff.Property(value => value.EnergyPricePerKwh).HasPrecision(18, 4);
            tariff.Property(value => value.StartFee).HasPrecision(18, 2);
            tariff.Property(value => value.IdleFeePerHour).HasPrecision(18, 2);
            tariff.HasIndex(value => new { value.TenantId, value.Name }).IsUnique();
        });

        modelBuilder.Entity<Station>(station =>
        {
            station.Property(value => value.Name).HasMaxLength(120);
            station.HasIndex(value => new { value.TenantId, value.Name }).IsUnique();
        });

        modelBuilder.Entity<ChargingSession>(session =>
        {
            session.Property(value => value.EnergyKwh).HasPrecision(18, 3);
            session.HasIndex(value => new { value.TenantId, value.StartedAtUtc });
            session.HasIndex(value => new { value.StationId, value.ConnectorId });
        });

        modelBuilder.Entity<Invoice>(invoice =>
        {
            invoice.Property(value => value.Currency).HasMaxLength(3);
            invoice.Property(value => value.EnergyKwh).HasPrecision(18, 3);
            invoice.Property(value => value.EnergyAmount).HasPrecision(18, 2);
            invoice.Property(value => value.StartFeeAmount).HasPrecision(18, 2);
            invoice.Property(value => value.IdleFeeAmount).HasPrecision(18, 2);
            invoice.Property(value => value.Total).HasPrecision(18, 2);
            // One invoice per session is what makes the worker's retry safe.
            invoice.HasIndex(value => value.SessionId).IsUnique();
        });
    }
}
