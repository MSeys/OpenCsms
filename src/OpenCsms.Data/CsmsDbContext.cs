namespace OpenCsms.Data;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.Property(value => value.Email).HasMaxLength(256);
            user.Property(value => value.DisplayName).HasMaxLength(120);
            user.Property(value => value.PasswordHash).HasMaxLength(256);
            user.Property(value => value.Role).HasConversion<string>().HasMaxLength(16);
            // One sign-in address identifies one account, wherever the tenant's rows are scoped.
            user.HasIndex(value => value.Email).IsUnique();
            user.HasIndex(value => value.TenantId);
        });

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
            station.Property(value => value.ChargePointId).HasMaxLength(64);
            station.HasIndex(value => value.ChargePointId).IsUnique();
            station.HasIndex(value => new { value.TenantId, value.Name }).IsUnique();
        });

        modelBuilder.Entity<Connector>(connector =>
        {
            connector.HasKey(value => new { value.StationId, value.ConnectorId });
            connector.Property(value => value.Status).HasConversion<string>().HasMaxLength(32);
            connector.Property(value => value.ErrorCode).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<ChargingSession>(session =>
        {
            session.Property(value => value.MeterStartKwh).HasPrecision(18, 3);
            session.Property(value => value.EnergyKwh).HasPrecision(18, 3);
            // The OCPP transaction number is the CSMS's, assigned by the store so charges and
            // stop-transactions can be looked up by the number the charge point was told.
            session.Property(value => value.TransactionId).UseIdentityAlwaysColumn();
            session.HasIndex(value => value.TransactionId).IsUnique();
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
