namespace OpenCsms.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCsms.Domain;

/// <summary>Maps a station: bounded text, one OCPP identity, one name per tenant.</summary>
internal sealed class StationConfiguration : IEntityTypeConfiguration<Station>
{
    public void Configure(EntityTypeBuilder<Station> station)
    {
        station.Property(value => value.Name).HasMaxLength(120);
        station.Property(value => value.ChargePointId).HasMaxLength(64);
        station.HasIndex(value => value.ChargePointId).IsUnique();
        station.HasIndex(value => new { value.TenantId, value.Name }).IsUnique();
    }
}
