using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dtde.Benchmarks.Data;
using Dtde.Benchmarks.Entities;
using Dtde.EntityFramework.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Benchmarks.Comparisons;

/// <summary>
/// Benchmarks for nested property navigation: Customer -> Profile -> Preferences
/// and Product -> Details -> Attributes scenarios.
/// Tests deep object graph traversal performance between single table and sharded approaches.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class NestedPropertiesBenchmarks
{
    private SingleTableDbContext _singleContext = null!;
    private ShardedDbContext _shardedContext = null!;

    private List<Customer> _customers = null!;
    private List<ShardedCustomer> _shardedCustomers = null!;
    private List<CustomerProfile> _profiles = null!;
    private List<ShardedCustomerProfile> _shardedProfiles = null!;
    private List<Product> _products = null!;
    private List<ShardedProduct> _shardedProducts = null!;
    private List<ProductDetails> _productDetails = null!;
    private List<ShardedProductDetails> _shardedProductDetails = null!;

    [Params(5_000, 10_000)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Setup contexts
        var singleOptions = new DbContextOptionsBuilder<SingleTableDbContext>()
            .UseSqlite($"Data Source=nested_single_{EntityCount}.db")
            .Options;
        _singleContext = new SingleTableDbContext(singleOptions);
        _singleContext.Database.EnsureDeleted();
        _singleContext.Database.EnsureCreated();

        var shardedOptions = new DbContextOptionsBuilder<ShardedDbContext>()
            .UseSqlite($"Data Source=nested_sharded_{EntityCount}.db")
            .UseDtde()
            .Options;
        _shardedContext = new ShardedDbContext(shardedOptions);
        _shardedContext.Database.EnsureDeleted();
        _shardedContext.Database.EnsureCreated();

        GenerateAndSeedData();
    }

    private void GenerateAndSeedData()
    {
        const int batchSize = 2000;

        // Generate customers and profiles
        _customers = DataGenerator.GenerateCustomers(EntityCount);
        _shardedCustomers = DataGenerator.GenerateShardedCustomers(EntityCount);
        _profiles = DataGenerator.GenerateCustomerProfiles(_customers);
        _shardedProfiles = DataGenerator.GenerateShardedCustomerProfiles(_shardedCustomers);

        // Generate products with details
        _products = DataGenerator.GenerateProducts(EntityCount);
        _shardedProducts = DataGenerator.GenerateShardedProducts(EntityCount);
        _productDetails = DataGenerator.GenerateProductDetails(_products);
        _shardedProductDetails = DataGenerator.GenerateShardedProductDetails(_shardedProducts);

        // Seed single table context
        SeedBatched(_singleContext, _singleContext.Customers, _customers, batchSize);
        SeedBatched(_singleContext, _singleContext.CustomerProfiles, _profiles, batchSize);
        SeedBatched(_singleContext, _singleContext.Products, _products, batchSize);
        SeedBatched(_singleContext, _singleContext.ProductDetails, _productDetails, batchSize);

        // Seed sharded context
        SeedBatched(_shardedContext, _shardedContext.Customers, _shardedCustomers, batchSize);
        SeedBatched(_shardedContext, _shardedContext.CustomerProfiles, _shardedProfiles, batchSize);
        SeedBatched(_shardedContext, _shardedContext.Products, _shardedProducts, batchSize);
        SeedBatched(_shardedContext, _shardedContext.ProductDetails, _shardedProductDetails, batchSize);
    }

    private void SeedBatched<T>(DbContext context, DbSet<T> dbSet, List<T> items, int batchSize) where T : class
    {
        for (var i = 0; i < items.Count; i += batchSize)
        {
            var batch = items.Skip(i).Take(batchSize).ToList();
            dbSet.AddRange(batch);
            context.SaveChanges();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _singleContext?.Dispose();
        _shardedContext?.Dispose();

        // Clear SQLite connection pool to release file handles
        SqliteConnection.ClearAllPools();
        Thread.Sleep(100);

        try { File.Delete($"nested_single_{EntityCount}.db"); } catch { /* ignore */ }
        try { File.Delete($"nested_sharded_{EntityCount}.db"); } catch { /* ignore */ }
    }

    #region Single Level Navigation (Customer -> Profile)

    /// <summary>
    /// Single table: Navigate to Profile from Customer.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("SingleLevelNavigation")]
    public List<CustomerProfile> SingleTable_CustomerProfile()
    {
        return _singleContext.Customers
            .Include(c => c.Profile)
            .Where(c => c.Tier == CustomerTier.Gold)
            .Select(c => c.Profile!)
            .Take(100)
            .ToList();
    }

    /// <summary>
    /// Sharded: Navigate to Profile from sharded Customer.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("SingleLevelNavigation")]
    public List<ShardedCustomerProfile> Sharded_CustomerProfile()
    {
        return _shardedContext.Customers
            .Include(c => c.Profile)
            .Where(c => c.Tier == CustomerTier.Gold)
            .Select(c => c.Profile!)
            .Take(100)
            .ToList();
    }

    #endregion

    #region Deep Navigation (Customer -> Profile -> Preferences as JSON)

    /// <summary>
    /// Single table: Access nested JSON preferences through profile.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("DeepNavigation")]
    public List<(string CustomerName, string Theme, bool DarkMode)> SingleTable_DeepNavigation_Preferences()
    {
        var results = _singleContext.Customers
            .Include(c => c.Profile)
            .Where(c => c.Profile != null && c.Profile.Preferences != null)
            .Take(200)
            .ToList();

        return results
            .Where(c => c.Profile?.Preferences != null)
            .Select(c => (c.Name, c.Profile!.Preferences!.Theme ?? "default", c.Profile.Preferences.DarkMode))
            .ToList();
    }

    /// <summary>
    /// Sharded: Access nested JSON preferences through sharded profile.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("DeepNavigation")]
    public List<(string CustomerName, string Theme, bool DarkMode)> Sharded_DeepNavigation_Preferences()
    {
        var results = _shardedContext.Customers
            .Include(c => c.Profile)
            .Where(c => c.Profile != null && c.Profile.Preferences != null)
            .Take(200)
            .ToList();

        return results
            .Where(c => c.Profile?.Preferences != null)
            .Select(c => (c.Name, c.Profile!.Preferences!.Theme ?? "default", c.Profile.Preferences.DarkMode))
            .ToList();
    }

    #endregion

    #region Product Details Navigation

    /// <summary>
    /// Single table: Navigate to ProductDetails.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ProductDetailsNavigation")]
    public List<ProductDetails> SingleTable_ProductDetails()
    {
        return _singleContext.Products
            .Include(p => p.Details)
            .Where(p => p.Category == "Electronics")
            .Select(p => p.Details!)
            .Take(100)
            .ToList();
    }

    /// <summary>
    /// Sharded: Navigate to ProductDetails in sharded context.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ProductDetailsNavigation")]
    public List<ShardedProductDetails> Sharded_ProductDetails()
    {
        return _shardedContext.Products
            .Include(p => p.Details)
            .Where(p => p.Category == "Electronics")
            .Select(p => p.Details!)
            .Take(100)
            .ToList();
    }

    #endregion

    #region Product with Attributes (Deep JSON Navigation)

    /// <summary>
    /// Single table: Access product attributes through details.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ProductAttributesNavigation")]
    public List<(string ProductName, int AttributeCount)> SingleTable_ProductAttributes()
    {
        var results = _singleContext.Products
            .Include(p => p.Details)
            .Where(p => p.Details != null && p.Details.Attributes != null)
            .Take(150)
            .ToList();

        return results
            .Where(p => p.Details?.Attributes != null)
            .Select(p => (p.Name, p.Details!.Attributes!.Count))
            .ToList();
    }

    /// <summary>
    /// Sharded: Access product attributes through sharded details.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ProductAttributesNavigation")]
    public List<(string ProductName, int AttributeCount)> Sharded_ProductAttributes()
    {
        var results = _shardedContext.Products
            .Include(p => p.Details)
            .Where(p => p.Details != null && p.Details.Attributes != null)
            .Take(150)
            .ToList();

        return results
            .Where(p => p.Details?.Attributes != null)
            .Select(p => (p.Name, p.Details!.Attributes!.Count))
            .ToList();
    }

    #endregion

    #region Full Object Graph Loading

    /// <summary>
    /// Single table: Load full customer graph (Customer + Profile + Orders).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("FullObjectGraph")]
    public List<Customer> SingleTable_FullCustomerGraph()
    {
        return _singleContext.Customers
            .Include(c => c.Profile)
            .Include(c => c.Orders)
            .Where(c => c.Region == "US")
            .Take(50)
            .ToList();
    }

    /// <summary>
    /// Sharded: Load full sharded customer graph.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("FullObjectGraph")]
    public List<ShardedCustomer> Sharded_FullCustomerGraph()
    {
        return _shardedContext.Customers
            .Include(c => c.Profile)
            .Include(c => c.Orders)
            .Where(c => c.Region == "US")
            .Take(50)
            .ToList();
    }

    #endregion

    #region Projection from Nested Properties

    /// <summary>
    /// Single table: Project specific fields from nested objects.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("NestedProjection")]
    public List<CustomerSummaryDto> SingleTable_NestedProjection()
    {
        return _singleContext.Customers
            .Where(c => c.Profile != null)
            .Select(c => new CustomerSummaryDto
            {
                Name = c.Name,
                Email = c.Email,
                Bio = c.Profile!.Bio ?? string.Empty,
                LoyaltyPoints = c.Profile.LoyaltyPoints,
                Theme = c.Profile.Preferences != null ? c.Profile.Preferences.Theme ?? "default" : "default"
            })
            .Take(200)
            .ToList();
    }

    /// <summary>
    /// Sharded: Project specific fields from sharded nested objects.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("NestedProjection")]
    public List<CustomerSummaryDto> Sharded_NestedProjection()
    {
        return _shardedContext.Customers
            .Where(c => c.Profile != null)
            .Select(c => new CustomerSummaryDto
            {
                Name = c.Name,
                Email = c.Email,
                Bio = c.Profile!.Bio ?? string.Empty,
                LoyaltyPoints = c.Profile.LoyaltyPoints,
                Theme = c.Profile.Preferences != null ? c.Profile.Preferences.Theme ?? "default" : "default"
            })
            .Take(200)
            .ToList();
    }

    #endregion

    #region Filter by Nested Property

    /// <summary>
    /// Single table: Filter by nested property value.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("FilterByNestedProperty")]
    public List<Customer> SingleTable_FilterByNestedProperty()
    {
        return _singleContext.Customers
            .Include(c => c.Profile)
            .Where(c => c.Profile != null && c.Profile.LoyaltyPoints > 1000)
            .Take(100)
            .ToList();
    }

    /// <summary>
    /// Sharded: Filter by nested property in sharded context.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("FilterByNestedProperty")]
    public List<ShardedCustomer> Sharded_FilterByNestedProperty()
    {
        return _shardedContext.Customers
            .Include(c => c.Profile)
            .Where(c => c.Profile != null && c.Profile.LoyaltyPoints > 1000)
            .Take(100)
            .ToList();
    }

    #endregion

    #region Aggregation on Nested Properties

    /// <summary>
    /// Single table: Aggregate on nested property.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("NestedAggregation")]
    public (int TotalPoints, double AvgPoints) SingleTable_NestedAggregation()
    {
        var result = _singleContext.Customers
            .Where(c => c.Profile != null)
            .Select(c => c.Profile!.LoyaltyPoints)
            .ToList();

        return (result.Sum(), result.Count > 0 ? result.Average() : 0);
    }

    /// <summary>
    /// Sharded: Aggregate on nested property in sharded context.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("NestedAggregation")]
    public (int TotalPoints, double AvgPoints) Sharded_NestedAggregation()
    {
        var result = _shardedContext.Customers
            .Where(c => c.Profile != null)
            .Select(c => c.Profile!.LoyaltyPoints)
            .ToList();

        return (result.Sum(), result.Count > 0 ? result.Average() : 0);
    }

    #endregion

    #region OrderBy Nested Property

    /// <summary>
    /// Single table: Order by nested property.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("OrderByNested")]
    public List<Customer> SingleTable_OrderByNestedProperty()
    {
        return _singleContext.Customers
            .Include(c => c.Profile)
            .Where(c => c.Profile != null)
            .OrderByDescending(c => c.Profile!.LoyaltyPoints)
            .Take(100)
            .ToList();
    }

    /// <summary>
    /// Sharded: Order by nested property in sharded context.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("OrderByNested")]
    public List<ShardedCustomer> Sharded_OrderByNestedProperty()
    {
        return _shardedContext.Customers
            .Include(c => c.Profile)
            .Where(c => c.Profile != null)
            .OrderByDescending(c => c.Profile!.LoyaltyPoints)
            .Take(100)
            .ToList();
    }

    #endregion
}

/// <summary>
/// DTO for projected customer summary.
/// </summary>
public sealed class CustomerSummaryDto
{
    public required string Name { get; init; }
    public required string Email { get; init; }
    public required string Bio { get; init; }
    public int LoyaltyPoints { get; init; }
    public required string Theme { get; init; }
}
