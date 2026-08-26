using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockyard.Domain;

namespace Stockyard.Infrastructure.Persistence.Configurations;

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public const string NumberSequence = "PurchaseOrderNumber";

    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("PurchaseOrder");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(o => o.Number).HasMaxLength(20);
        builder.HasIndex(o => o.Number).IsUnique();
        builder.Property(o => o.Status).HasConversion<byte>();
        builder.Property(o => o.IdempotencyKey).HasMaxLength(64);
        builder.HasIndex(o => o.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
        builder.Property(o => o.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(o => o.RowVersion).IsRowVersion();

        builder.HasOne(o => o.Supplier).WithMany().HasForeignKey(o => o.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.JobSite).WithMany().HasForeignKey(o => o.JobSiteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
    }
}
