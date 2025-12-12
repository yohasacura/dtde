using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
using Dtde.Samples.RegionSharding.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.RegionSharding.Data;

/// <summary>
/// DbContext demonstrating region-based table sharding.
/// 
/// This context routes entities to different tables based on the Region property:
/// - Customers_EU, Customers_US, Customers_APAC
/// - Orders_EU, Orders_US, Orders_APAC
/// - OrderItems_EU, OrderItems_US, OrderItems_APAC
/// </summary>
public class RegionShardingDbContext : DtdeDbContext
{
    public RegionShardingDbContext(DbContextOptions<RegionShardingDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Customers - sharded by Region.
    /// </summary>
    public DbSet<Customer> Customers => Set<Customer>();

    /// <summary>
    /// Orders - sharded by Region (denormalized from Customer).
    /// </summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <summary>
    /// Order items - sharded by Region.
    /// </summary>
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Customer entity
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Region).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Phone).HasMaxLength(50);
            entity.Property(e => e.Address).HasMaxLength(500);

            entity.HasIndex(e => e.Email);
            entity.HasIndex(e => e.Region);

            // DTDE: Configure sharding by Region property
            entity.ShardBy(c => c.Region);
        });

        // Configure Order entity
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OrderNumber).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Region).HasMaxLength(10).IsRequired();
            entity.Property(e => e.TotalAmount).HasPrecision(18, 2);
            entity.Property(e => e.Currency).HasMaxLength(3);
            entity.Property(e => e.ShippingAddress).HasMaxLength(500);

            entity.HasIndex(e => e.OrderNumber);
            entity.HasIndex(e => e.CustomerId);
            entity.HasIndex(e => e.Region);

            // Relationship to Customer
            entity.HasOne(e => e.Customer)
                .WithMany(c => c.Orders)
                .HasForeignKey(e => e.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            // DTDE: Configure sharding by Region property
            entity.ShardBy(o => o.Region);
        });

        // Configure OrderItem entity
        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Region).HasMaxLength(10).IsRequired();
            entity.Property(e => e.ProductSku).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ProductName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);

            entity.HasIndex(e => e.ProductSku);

            // Relationship to Order
            entity.HasOne(e => e.Order)
                .WithMany(o => o.Items)
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // DTDE: Configure sharding by Region property
            entity.ShardBy(i => i.Region);
        });
    }
}
