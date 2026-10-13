using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockyard.Domain;

namespace Stockyard.Infrastructure.Persistence.Configurations;

internal sealed class JobSiteConfiguration : IEntityTypeConfiguration<JobSite>
{
    public void Configure(EntityTypeBuilder<JobSite> builder)
    {
        builder.ToTable("JobSite");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(j => j.Code).HasMaxLength(Codes.CodeMaxLength);
        builder.HasIndex(j => j.Code).IsUnique();
        builder.Property(j => j.Name).HasMaxLength(JobSite.NameMaxLength);
    }
}
