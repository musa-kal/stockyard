using Microsoft.EntityFrameworkCore;
using Stockyard.Domain;

namespace Stockyard.Infrastructure.Persistence;

public sealed class StockyardDbContext(DbContextOptions<StockyardDbContext> options) : DbContext(options)
{
    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<Item> Items => Set<Item>();

    public DbSet<JobSite> JobSites => Set<JobSite>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dbo");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StockyardDbContext).Assembly);
    }
}
