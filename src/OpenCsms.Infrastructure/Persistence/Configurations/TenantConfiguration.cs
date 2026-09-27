namespace OpenCsms.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCsms.Domain;

/// <summary>Maps a machine tenant: bounded text and one tenant per key hash.</summary>
internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> tenant)
    {
        tenant.Property(value => value.Id).HasMaxLength(64);
        tenant.Property(value => value.Name).HasMaxLength(120);
        tenant.Property(value => value.ApiKeyHash).HasMaxLength(64);
        // One stored key hash identifies one tenant; a second registration cannot reuse it.
        tenant.HasIndex(value => value.ApiKeyHash).IsUnique();
    }
}
