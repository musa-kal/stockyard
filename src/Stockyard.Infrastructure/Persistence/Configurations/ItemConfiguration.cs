using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockyard.Domain;

namespace Stockyard.Infrastructure.Persistence.Configurations;

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("Item");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(i => i.Sku).HasMaxLength(Codes.SkuMaxLength);
        builder.HasIndex(i => i.Sku).IsUnique();
        builder.Property(i => i.Description).HasMaxLength(Item.DescriptionMaxLength);
        builder.Property(i => i.UnitOfMeasure).HasMaxLength(Item.UnitOfMeasureMaxLength);
        builder.Property(i => i.UnitCost).HasPrecision(18, 4);
        builder.Property(i => i.ReorderPoint).HasPrecision(18, 3);
        builder.Property(i => i.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
    }
}
