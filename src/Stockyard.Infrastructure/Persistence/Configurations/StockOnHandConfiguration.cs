using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockyard.Domain;

namespace Stockyard.Infrastructure.Persistence.Configurations;

internal sealed class StockOnHandConfiguration : IEntityTypeConfiguration<StockOnHand>
{
    public void Configure(EntityTypeBuilder<StockOnHand> builder)
    {
        builder.ToTable("StockOnHand");
        builder.HasKey(s => new { s.ItemId, s.JobSiteId });
        builder.Property(s => s.Quantity).HasPrecision(18, 3);

        builder.HasOne<Item>().WithMany().HasForeignKey(s => s.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JobSite>().WithMany().HasForeignKey(s => s.JobSiteId).OnDelete(DeleteBehavior.Restrict);
    }
}
