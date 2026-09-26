namespace OpenCsms.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCsms.Domain;

/// <summary>Maps a charging session: energy precision and the transaction number's identity.</summary>
internal sealed class ChargingSessionConfiguration : IEntityTypeConfiguration<ChargingSession>
{
    public void Configure(EntityTypeBuilder<ChargingSession> session)
    {
        session.Property(value => value.MeterStartKwh).HasPrecision(18, 3);
        session.Property(value => value.EnergyKwh).HasPrecision(18, 3);
        // The OCPP transaction number is the CSMS's, assigned by the store so charges and
        // stop-transactions can be looked up by the number the charge point was told.
        session.Property(value => value.TransactionId).UseIdentityAlwaysColumn();
        session.HasIndex(value => value.TransactionId).IsUnique();
        session.HasIndex(value => new { value.TenantId, value.StartedAtUtc });
        session.HasIndex(value => new { value.StationId, value.ConnectorId });
    }
}
