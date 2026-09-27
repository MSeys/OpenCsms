namespace OpenCsms.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

/// <summary>Maps an outbox row: the payload is opaque JSON and the pending set carries the dispatcher's index.</summary>
internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> message)
    {
        message.Property(value => value.RoutingKey).HasMaxLength(128);
        // The dispatcher's one query: rows that are not sent and are due, oldest first.
        message.HasIndex(value => new { value.SentAtUtc, value.NextAttemptAtUtc });
    }
}
