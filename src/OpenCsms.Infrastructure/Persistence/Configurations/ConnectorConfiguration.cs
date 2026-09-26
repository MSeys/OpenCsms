namespace OpenCsms.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCsms.Domain;

/// <summary>Maps the last status per connector: the station-and-connector pair is the key.</summary>
internal sealed class ConnectorConfiguration : IEntityTypeConfiguration<Connector>
{
    public void Configure(EntityTypeBuilder<Connector> connector)
    {
        connector.HasKey(value => new { value.StationId, value.ConnectorId });
        connector.Property(value => value.Status).HasConversion<string>().HasMaxLength(32);
        connector.Property(value => value.ErrorCode).HasConversion<string>().HasMaxLength(32);
    }
}
