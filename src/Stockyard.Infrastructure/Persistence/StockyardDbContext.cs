using Microsoft.EntityFrameworkCore;
using Stockyard.Domain;

namespace Stockyard.Infrastructure.Persistence;

public sealed class StockyardDbContext(DbContextOptions<StockyardDbContext> options) : DbContext(options)
{
    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<Item> Items => Set<Item>();

    public DbSet<JobSite> JobSites => Set<JobSite>();

    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();

    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dbo");
        modelBuilder.HasSequence<long>(Configurations.PurchaseOrderConfiguration.NumberSequence).StartsAt(1000);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StockyardDbContext).Assembly);
    }
}
