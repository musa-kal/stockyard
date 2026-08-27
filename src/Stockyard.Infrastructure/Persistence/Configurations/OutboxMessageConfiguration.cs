using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockyard.Domain;

namespace Stockyard.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessage");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(o => o.Type).HasMaxLength(200);
        builder.Property(o => o.OccurredAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(o => o.LastError).HasMaxLength(1000);

        // The relay only ever reads unpublished rows, oldest first.
        builder.HasIndex(o => o.OccurredAt)
            .HasDatabaseName("IX_OutboxMessage_Unpublished")
            .HasFilter("[PublishedAt] IS NULL")
            .IncludeProperties(o => o.Type);
    }
}
