namespace OpenCsms.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCsms.Domain;

/// <summary>Maps a tariff: bounded text, money precision, one name per tenant.</summary>
internal sealed class TariffConfiguration : IEntityTypeConfiguration<Tariff>
{
    public void Configure(EntityTypeBuilder<Tariff> tariff)
    {
        tariff.Property(value => value.Name).HasMaxLength(120);
        tariff.Property(value => value.Currency).HasMaxLength(3);
        tariff.Property(value => value.EnergyPricePerKwh).HasPrecision(18, 4);
        tariff.Property(value => value.StartFee).HasPrecision(18, 2);
        tariff.Property(value => value.IdleFeePerHour).HasPrecision(18, 2);
        tariff.HasIndex(value => new { value.TenantId, value.Name }).IsUnique();
    }
}
