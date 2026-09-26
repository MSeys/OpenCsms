namespace OpenCsms.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCsms.Domain;

/// <summary>Maps the invoice: money precision and one invoice per session.</summary>
internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> invoice)
    {
        invoice.Property(value => value.Currency).HasMaxLength(3);
        invoice.Property(value => value.EnergyKwh).HasPrecision(18, 3);
        invoice.Property(value => value.EnergyAmount).HasPrecision(18, 2);
        invoice.Property(value => value.StartFeeAmount).HasPrecision(18, 2);
        invoice.Property(value => value.IdleFeeAmount).HasPrecision(18, 2);
        invoice.Property(value => value.Total).HasPrecision(18, 2);
        // One invoice per session is what makes the worker's retry safe.
        invoice.HasIndex(value => value.SessionId).IsUnique();
    }
}
