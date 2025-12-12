using Dtde.Abstractions.Metadata;
using Dtde.Benchmarks.Entities;
using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Benchmarks.Data;

/// <summary>
/// Sharded DbContext using DTDE - demonstrates various sharding strategies.
/// </summary>
public class ShardedDbContext : DtdeDbContext
{
    public ShardedDbContext(DbContextOptions<ShardedDbContext> options)
        : base(options)
    {
    }

    public DbSet<ShardedCustomer> Customers => Set<ShardedCustomer>();
    public DbSet<ShardedCustomerProfile> CustomerProfiles => Set<ShardedCustomerProfile>();
    public DbSet<ShardedCustomerPreferences> CustomerPreferences => Set<ShardedCustomerPreferences>();
    public DbSet<ShardedOrder> Orders => Set<ShardedOrder>();
    public DbSet<ShardedOrderItem> OrderItems => Set<ShardedOrderItem>();
    public DbSet<ShardedProduct> Products => Set<ShardedProduct>();
    public DbSet<ShardedProductDetails> ProductDetails => Set<ShardedProductDetails>();
    public DbSet<ShardedProductAttribute> ProductAttributes => Set<ShardedProductAttribute>();
    public DbSet<ShardedTransaction> Transactions => Set<ShardedTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Customer - Property-based sharding by Region
        modelBuilder.Entity<ShardedCustomer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Region).HasMaxLength(50).IsRequired();

            // Configure sharding by Region property
            entity.ShardBy(c => c.Region)
                .WithStorageMode(ShardStorageMode.Tables);

            // Indexes on sharded tables
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.Region);
            entity.HasIndex(e => e.CreatedAt);

            entity.HasOne(e => e.Profile)
                .WithOne(p => p.Customer)
                .HasForeignKey<ShardedCustomerProfile>(p => p.CustomerId);

            entity.HasMany(e => e.Orders)
                .WithOne(o => o.Customer)
                .HasForeignKey(o => o.CustomerId);
        });

        // Customer profile - Co-located with customer by region
        modelBuilder.Entity<ShardedCustomerProfile>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.ShardBy(p => p.Region)
                .WithStorageMode(ShardStorageMode.Tables);

            entity.HasIndex(e => e.CustomerId).IsUnique();

            entity.HasOne(e => e.Preferences)
                .WithOne(p => p.Profile)
                .HasForeignKey<ShardedCustomerPreferences>(p => p.CustomerProfileId);
        });

        // Customer preferences - Co-located
        modelBuilder.Entity<ShardedCustomerPreferences>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CustomerProfileId).IsUnique();
        });

        // Order - Property-based sharding by Region (co-located with customer)
        modelBuilder.Entity<ShardedOrder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OrderNumber).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Region).HasMaxLength(50).IsRequired();
            entity.Property(e => e.TotalAmount).HasPrecision(18, 2);

            // Sharding by region for co-location with customers
            entity.ShardBy(o => o.Region)
                .WithStorageMode(ShardStorageMode.Tables);

            // Indexes
            entity.HasIndex(e => e.OrderNumber).IsUnique();
            entity.HasIndex(e => e.OrderDate);
            entity.HasIndex(e => e.CustomerId);
            entity.HasIndex(e => new { e.Region, e.OrderDate });

            entity.HasMany(e => e.Items)
                .WithOne(i => i.Order)
                .HasForeignKey(i => i.OrderId);
        });

        // Order item - Co-located with order by region
        modelBuilder.Entity<ShardedOrderItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProductSku).HasMaxLength(50).IsRequired();
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
            entity.Property(e => e.Discount).HasPrecision(18, 2);
            entity.Property(e => e.Region).HasMaxLength(50).IsRequired();

            entity.ShardBy(i => i.Region)
                .WithStorageMode(ShardStorageMode.Tables);

            entity.HasIndex(e => e.OrderId);
            entity.HasIndex(e => e.ProductSku);
        });

        // Product - Hash-based sharding for even distribution
        modelBuilder.Entity<ShardedProduct>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Sku).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Price).HasPrecision(18, 2);

            // Hash-based sharding across 8 shards
            entity.ShardByHash(p => p.Id, shardCount: 8)
                .WithStorageMode(ShardStorageMode.Tables);

            entity.HasIndex(e => e.Sku).IsUnique();
            entity.HasIndex(e => e.Category);

            entity.HasOne(e => e.Details)
                .WithOne(d => d.Product)
                .HasForeignKey<ShardedProductDetails>(d => d.ProductId);

            entity.HasMany(e => e.OrderItems)
                .WithOne(i => i.Product)
                .HasForeignKey(i => i.ProductSku)
                .HasPrincipalKey(p => p.Sku);
        });

        // Product details - Co-located with product
        modelBuilder.Entity<ShardedProductDetails>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ProductId).IsUnique();

            entity.HasMany(e => e.Attributes)
                .WithOne(a => a.ProductDetails)
                .HasForeignKey(a => a.ProductDetailsId);
        });

        // Product attribute
        modelBuilder.Entity<ShardedProductAttribute>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ProductDetailsId);
        });

        // Transaction - Date-based sharding by TransactionDate (monthly)
        modelBuilder.Entity<ShardedTransaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TransactionRef).HasMaxLength(50).IsRequired();
            entity.Property(e => e.AccountNumber).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.BalanceBefore).HasPrecision(18, 2);
            entity.Property(e => e.BalanceAfter).HasPrecision(18, 2);

            // Date-based sharding by month
            entity.ShardByDate(t => t.TransactionDate, DateShardInterval.Month)
                .WithStorageMode(ShardStorageMode.Tables);

            entity.HasIndex(e => e.TransactionRef).IsUnique();
            entity.HasIndex(e => e.AccountNumber);
            entity.HasIndex(e => e.TransactionDate);
            entity.HasIndex(e => new { e.AccountNumber, e.TransactionDate });
        });
    }
}

/// <summary>
/// Simulated multi-server sharded context for distributed benchmarks.
/// This simulates having data across multiple database servers.
/// </summary>
public class MultiServerShardedDbContext : DtdeDbContext
{
    private readonly string _serverIdentifier;

    public MultiServerShardedDbContext(DbContextOptions<MultiServerShardedDbContext> options, string serverIdentifier = "primary")
        : base(options)
    {
        _serverIdentifier = serverIdentifier;
    }

    public DbSet<ShardedTransaction> Transactions => Set<ShardedTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Use different table naming based on server
        modelBuilder.Entity<ShardedTransaction>(entity =>
        {
            entity.ToTable($"Transactions_{_serverIdentifier}");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.TransactionRef).HasMaxLength(50).IsRequired();
            entity.Property(e => e.AccountNumber).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(18, 2);

            entity.ShardByDate(t => t.TransactionDate, DateShardInterval.Month)
                .WithStorageMode(ShardStorageMode.Tables);

            entity.HasIndex(e => e.TransactionRef).IsUnique();
            entity.HasIndex(e => e.AccountNumber);
            entity.HasIndex(e => e.TransactionDate);
        });
    }
}
