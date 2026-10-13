using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockyard.Domain;

namespace Stockyard.Infrastructure.Persistence.Configurations;

internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.ToTable("PurchaseOrderLine");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.HasIndex(l => new { l.PurchaseOrderId, l.LineNumber }).IsUnique();
        builder.Property(l => l.QuantityOrdered).HasPrecision(18, 3);
        builder.Property(l => l.QuantityReceived).HasPrecision(18, 3);
        builder.Property(l => l.UnitCost).HasPrecision(18, 4);
        builder.Property(l => l.LineTotal)
            .HasPrecision(37, 7)
            .HasComputedColumnSql("[QuantityOrdered] * [UnitCost]", stored: true);

        builder.HasOne(l => l.Item).WithMany().HasForeignKey(l => l.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
