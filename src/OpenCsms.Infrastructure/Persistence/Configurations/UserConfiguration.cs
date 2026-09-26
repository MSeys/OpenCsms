namespace OpenCsms.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCsms.Domain;

/// <summary>Maps a dashboard account: bounded text, the role as text, one address per account.</summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> user)
    {
        user.Property(value => value.Email).HasMaxLength(256);
        user.Property(value => value.DisplayName).HasMaxLength(120);
        user.Property(value => value.PasswordHash).HasMaxLength(256);
        user.Property(value => value.Role).HasConversion<string>().HasMaxLength(16);
        // One sign-in address identifies one account, wherever the tenant's rows are scoped.
        user.HasIndex(value => value.Email).IsUnique();
        user.HasIndex(value => value.TenantId);
    }
}
