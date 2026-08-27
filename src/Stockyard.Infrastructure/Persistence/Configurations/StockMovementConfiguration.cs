using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockyard.Domain;

namespace Stockyard.Infrastructure.Persistence.Configurations;

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovement");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).UseIdentityColumn();
        builder.Property(m => m.Kind).HasConversion<byte>();
        builder.Property(m => m.Quantity).HasPrecision(18, 3);
        builder.Property(m => m.Reference).HasMaxLength(40);
        builder.Property(m => m.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne<Item>().WithMany().HasForeignKey(m => m.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JobSite>().WithMany().HasForeignKey(m => m.JobSiteId).OnDelete(DeleteBehavior.Restrict);
    }
}
