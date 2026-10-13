using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockyard.Domain;

namespace Stockyard.Infrastructure.Persistence.Configurations;

internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("WebhookDelivery");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(w => w.EventType).HasMaxLength(100);
        builder.Property(w => w.LastError).HasMaxLength(1000);

        // The notifier claims undelivered rows whose next attempt is due.
        builder.HasIndex(w => w.NextAttemptAt)
            .HasDatabaseName("IX_WebhookDelivery_Pending")
            .HasFilter("[DeliveredAt] IS NULL");

        builder.HasOne<Supplier>().WithMany().HasForeignKey(w => w.SupplierId).OnDelete(DeleteBehavior.Restrict);
    }
}
