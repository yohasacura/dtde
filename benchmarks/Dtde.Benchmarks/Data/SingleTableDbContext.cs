using Dtde.Benchmarks.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Benchmarks.Data;

/// <summary>
/// Single table DbContext - no sharding, traditional approach.
/// Used as baseline for comparison.
/// </summary>
public class SingleTableDbContext : DbContext
{
    public SingleTableDbContext(DbContextOptions<SingleTableDbContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public DbSet<CustomerPreferences> CustomerPreferences => Set<CustomerPreferences>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductDetails> ProductDetails => Set<ProductDetails>();
    public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Customer configuration
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Region).HasMaxLength(50).IsRequired();

            entity.HasOne(e => e.Profile)
                .WithOne(p => p.Customer)
                .HasForeignKey<CustomerProfile>(p => p.CustomerId);

            entity.HasMany(e => e.Orders)
                .WithOne(o => o.Customer)
                .HasForeignKey(o => o.CustomerId);
        });

        // Customer profile configuration
        modelBuilder.Entity<CustomerProfile>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Preferences)
                .WithOne(p => p.Profile)
                .HasForeignKey<CustomerPreferences>(p => p.CustomerProfileId);
        });

        // Order configuration
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OrderNumber).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Region).HasMaxLength(50).IsRequired();
            entity.Property(e => e.TotalAmount).HasPrecision(18, 2);

            entity.HasMany(e => e.Items)
                .WithOne(i => i.Order)
                .HasForeignKey(i => i.OrderId);
        });

        // Order item configuration
        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProductSku).HasMaxLength(50).IsRequired();
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
            entity.Property(e => e.Discount).HasPrecision(18, 2);
        });

        // Product configuration
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Sku).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Price).HasPrecision(18, 2);

            entity.HasOne(e => e.Details)
                .WithOne(d => d.Product)
                .HasForeignKey<ProductDetails>(d => d.ProductId);

            entity.HasMany(e => e.OrderItems)
                .WithOne(i => i.Product)
                .HasForeignKey(i => i.ProductSku)
                .HasPrincipalKey(p => p.Sku);
        });

        // Product details configuration
        modelBuilder.Entity<ProductDetails>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasMany(e => e.Attributes)
                .WithOne(a => a.ProductDetails)
                .HasForeignKey(a => a.ProductDetailsId);
        });

        // Transaction configuration
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TransactionRef).HasMaxLength(50).IsRequired();
            entity.Property(e => e.AccountNumber).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.BalanceBefore).HasPrecision(18, 2);
            entity.Property(e => e.BalanceAfter).HasPrecision(18, 2);
        });
    }
}

/// <summary>
/// Single table DbContext with indexes - for indexed vs non-indexed comparison.
/// </summary>
public class IndexedSingleTableDbContext : DbContext
{
    public IndexedSingleTableDbContext(DbContextOptions<IndexedSingleTableDbContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public DbSet<CustomerPreferences> CustomerPreferences => Set<CustomerPreferences>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductDetails> ProductDetails => Set<ProductDetails>();
    public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Customer configuration with indexes
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Region).HasMaxLength(50).IsRequired();

            // Indexes for common query patterns
            entity.HasIndex(e => e.Region).HasDatabaseName("IX_Customers_Region");
            entity.HasIndex(e => e.Email).IsUnique().HasDatabaseName("IX_Customers_Email");
            entity.HasIndex(e => e.CreatedAt).HasDatabaseName("IX_Customers_CreatedAt");
            entity.HasIndex(e => new { e.Region, e.Tier }).HasDatabaseName("IX_Customers_Region_Tier");

            entity.HasOne(e => e.Profile)
                .WithOne(p => p.Customer)
                .HasForeignKey<CustomerProfile>(p => p.CustomerId);

            entity.HasMany(e => e.Orders)
                .WithOne(o => o.Customer)
                .HasForeignKey(o => o.CustomerId);
        });

        // Customer profile configuration
        modelBuilder.Entity<CustomerProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CustomerId).IsUnique();

            entity.HasOne(e => e.Preferences)
                .WithOne(p => p.Profile)
                .HasForeignKey<CustomerPreferences>(p => p.CustomerProfileId);
        });

        // Order configuration with indexes
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OrderNumber).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Region).HasMaxLength(50).IsRequired();
            entity.Property(e => e.TotalAmount).HasPrecision(18, 2);

            // Indexes for common query patterns
            entity.HasIndex(e => e.OrderNumber).IsUnique().HasDatabaseName("IX_Orders_OrderNumber");
            entity.HasIndex(e => e.Region).HasDatabaseName("IX_Orders_Region");
            entity.HasIndex(e => e.OrderDate).HasDatabaseName("IX_Orders_OrderDate");
            entity.HasIndex(e => e.CustomerId).HasDatabaseName("IX_Orders_CustomerId");
            entity.HasIndex(e => new { e.Region, e.OrderDate }).HasDatabaseName("IX_Orders_Region_OrderDate");
            entity.HasIndex(e => new { e.CustomerId, e.OrderDate }).HasDatabaseName("IX_Orders_Customer_OrderDate");
            entity.HasIndex(e => e.Status).HasDatabaseName("IX_Orders_Status");

            entity.HasMany(e => e.Items)
                .WithOne(i => i.Order)
                .HasForeignKey(i => i.OrderId);
        });

        // Order item configuration with indexes
        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProductSku).HasMaxLength(50).IsRequired();
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
            entity.Property(e => e.Discount).HasPrecision(18, 2);

            entity.HasIndex(e => e.OrderId).HasDatabaseName("IX_OrderItems_OrderId");
            entity.HasIndex(e => e.ProductSku).HasDatabaseName("IX_OrderItems_ProductSku");
        });

        // Product configuration with indexes
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Sku).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Price).HasPrecision(18, 2);

            entity.HasIndex(e => e.Sku).IsUnique().HasDatabaseName("IX_Products_Sku");
            entity.HasIndex(e => e.Category).HasDatabaseName("IX_Products_Category");
            entity.HasIndex(e => new { e.Category, e.SubCategory }).HasDatabaseName("IX_Products_Category_SubCategory");
            entity.HasIndex(e => e.Price).HasDatabaseName("IX_Products_Price");

            entity.HasOne(e => e.Details)
                .WithOne(d => d.Product)
                .HasForeignKey<ProductDetails>(d => d.ProductId);

            entity.HasMany(e => e.OrderItems)
                .WithOne(i => i.Product)
                .HasForeignKey(i => i.ProductSku)
                .HasPrincipalKey(p => p.Sku);
        });

        // Product details configuration
        modelBuilder.Entity<ProductDetails>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ProductId).IsUnique();

            entity.HasMany(e => e.Attributes)
                .WithOne(a => a.ProductDetails)
                .HasForeignKey(a => a.ProductDetailsId);
        });

        // Product attribute configuration
        modelBuilder.Entity<ProductAttribute>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ProductDetailsId).HasDatabaseName("IX_ProductAttributes_ProductDetailsId");
            entity.HasIndex(e => new { e.ProductDetailsId, e.Name }).HasDatabaseName("IX_ProductAttributes_Details_Name");
        });

        // Transaction configuration with indexes
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TransactionRef).HasMaxLength(50).IsRequired();
            entity.Property(e => e.AccountNumber).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.BalanceBefore).HasPrecision(18, 2);
            entity.Property(e => e.BalanceAfter).HasPrecision(18, 2);

            // Indexes for common query patterns
            entity.HasIndex(e => e.TransactionRef).IsUnique().HasDatabaseName("IX_Transactions_Ref");
            entity.HasIndex(e => e.AccountNumber).HasDatabaseName("IX_Transactions_AccountNumber");
            entity.HasIndex(e => e.TransactionDate).HasDatabaseName("IX_Transactions_TransactionDate");
            entity.HasIndex(e => new { e.AccountNumber, e.TransactionDate }).HasDatabaseName("IX_Transactions_Account_Date");
            entity.HasIndex(e => new { e.TransactionDate, e.Type }).HasDatabaseName("IX_Transactions_Date_Type");
        });
    }
}
