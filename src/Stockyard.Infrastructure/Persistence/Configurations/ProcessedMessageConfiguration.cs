using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockyard.Domain;

namespace Stockyard.Infrastructure.Persistence.Configurations;

internal sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("ProcessedMessage");
        builder.HasKey(p => new { p.ConsumerName, p.MessageId });
        builder.Property(p => p.ConsumerName).HasMaxLength(100);
        builder.Property(p => p.ProcessedAt).HasDefaultValueSql("SYSUTCDATETIME()");
    }
}
