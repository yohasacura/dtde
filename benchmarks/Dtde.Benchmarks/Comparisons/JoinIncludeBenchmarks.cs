using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dtde.Benchmarks.Data;
using Dtde.Benchmarks.Entities;
using Dtde.EntityFramework.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Benchmarks.Comparisons;

/// <summary>
/// Benchmarks comparing JOIN and Include operations between single table and sharded scenarios.
/// This is critical for understanding the performance implications of related data access patterns.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class JoinIncludeBenchmarks
{
    private SingleTableDbContext _singleContext = null!;
    private IndexedSingleTableDbContext _indexedContext = null!;
    private ShardedDbContext _shardedContext = null!;

    private List<Customer> _customers = null!;
    private List<ShardedCustomer> _shardedCustomers = null!;
    private List<Order> _orders = null!;
    private List<ShardedOrder> _shardedOrders = null!;
    private List<Product> _products = null!;
    private List<ShardedProduct> _shardedProducts = null!;
    private List<OrderItem> _orderItems = null!;
    private List<ShardedOrderItem> _shardedOrderItems = null!;

    public static IEnumerable<int> CustomerCountSource => BenchmarkConfig.JoinIncludeRecordCounts;

    [ParamsSource(nameof(CustomerCountSource))]
    public int CustomerCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Setup contexts
        var singleOptions = new DbContextOptionsBuilder<SingleTableDbContext>()
            .UseSqlite($"Data Source=join_single_{CustomerCount}.db")
            .Options;
        _singleContext = new SingleTableDbContext(singleOptions);
        _singleContext.Database.EnsureDeleted();
        _singleContext.Database.EnsureCreated();

        var indexedOptions = new DbContextOptionsBuilder<IndexedSingleTableDbContext>()
            .UseSqlite($"Data Source=join_indexed_{CustomerCount}.db")
            .Options;
        _indexedContext = new IndexedSingleTableDbContext(indexedOptions);
        _indexedContext.Database.EnsureDeleted();
        _indexedContext.Database.EnsureCreated();

        var shardedOptions = new DbContextOptionsBuilder<ShardedDbContext>()
            .UseSqlite($"Data Source=join_sharded_{CustomerCount}.db")
            .UseDtde()
            .Options;
        _shardedContext = new ShardedDbContext(shardedOptions);
        _shardedContext.Database.EnsureDeleted();
        _shardedContext.Database.EnsureCreated();

        // Generate data
        _customers = DataGenerator.GenerateCustomers(CustomerCount);
        _shardedCustomers = DataGenerator.GenerateShardedCustomers(CustomerCount);
        _orders = DataGenerator.GenerateOrders(CustomerCount * 5, _customers);
        _shardedOrders = DataGenerator.GenerateShardedOrders(CustomerCount * 5, _shardedCustomers);
        _products = DataGenerator.GenerateProducts(1000);
        _shardedProducts = DataGenerator.GenerateShardedProducts(1000);
        _orderItems = DataGenerator.GenerateOrderItems(_orders, _products);
        _shardedOrderItems = DataGenerator.GenerateShardedOrderItems(_shardedOrders, _shardedProducts);

        SeedData();
    }

    private void SeedData()
    {
        const int batchSize = 5000;

        // Seed single table context
        SeedBatched(_singleContext.Customers, _customers, batchSize, _singleContext);
        SeedBatched(_singleContext.Products, _products, batchSize, _singleContext);
        SeedBatched(_singleContext.Orders, _orders, batchSize, _singleContext);
        SeedBatched(_singleContext.OrderItems, _orderItems, batchSize, _singleContext);

        // Seed indexed context (clone data)
        var idxCustomers = _customers.Select(c => new Customer
        {
            Name = c.Name,
            Email = c.Email + ".join",
            Region = c.Region,
            Phone = c.Phone,
            Address = c.Address,
            City = c.City,
            Country = c.Country,
            Tier = c.Tier,
            CreatedAt = c.CreatedAt,
            IsActive = c.IsActive
        }).ToList();
        SeedBatched(_indexedContext.Customers, idxCustomers, batchSize, _indexedContext);

        var idxProducts = _products.Select(p => new Product
        {
            Sku = p.Sku + "-join",
            Name = p.Name,
            Category = p.Category,
            SubCategory = p.SubCategory,
            Price = p.Price,
            StockQuantity = p.StockQuantity,
            Description = p.Description,
            IsActive = p.IsActive,
            CreatedAt = p.CreatedAt
        }).ToList();
        SeedBatched(_indexedContext.Products, idxProducts, batchSize, _indexedContext);

        var idxOrders = _orders.Select(o => new Order
        {
            OrderNumber = o.OrderNumber + "-join",
            CustomerId = o.CustomerId,
            Region = o.Region,
            OrderDate = o.OrderDate,
            TotalAmount = o.TotalAmount,
            Currency = o.Currency,
            Status = o.Status,
            ShippingAddress = o.ShippingAddress,
            CreatedAt = o.CreatedAt,
            ProcessedAt = o.ProcessedAt
        }).ToList();
        SeedBatched(_indexedContext.Orders, idxOrders, batchSize, _indexedContext);

        var idxItems = _orderItems.Select(i => new OrderItem
        {
            OrderId = i.OrderId,
            ProductSku = i.ProductSku + "-join",
            ProductName = i.ProductName,
            Quantity = i.Quantity,
            UnitPrice = i.UnitPrice,
            Discount = i.Discount
        }).ToList();
        SeedBatched(_indexedContext.OrderItems, idxItems, batchSize, _indexedContext);

        // Seed sharded context
        SeedBatched(_shardedContext.Customers, _shardedCustomers, batchSize, _shardedContext);
        SeedBatched(_shardedContext.Products, _shardedProducts, batchSize, _shardedContext);
        SeedBatched(_shardedContext.Orders, _shardedOrders, batchSize, _shardedContext);
        SeedBatched(_shardedContext.OrderItems, _shardedOrderItems, batchSize, _shardedContext);
    }

    private void SeedBatched<T>(DbSet<T> dbSet, List<T> items, int batchSize, DbContext context) where T : class
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
        _indexedContext?.Dispose();
        _shardedContext?.Dispose();

        // Clear SQLite connection pool to release file handles
        SqliteConnection.ClearAllPools();
        Thread.Sleep(100);

        try { File.Delete($"join_single_{CustomerCount}.db"); } catch { /* ignore */ }
        try { File.Delete($"join_indexed_{CustomerCount}.db"); } catch { /* ignore */ }
        try { File.Delete($"join_sharded_{CustomerCount}.db"); } catch { /* ignore */ }
    }

    #region Simple Include (One-to-Many)

    /// <summary>
    /// Single table: Include orders for customers.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("SimpleInclude")]
    public List<Customer> SingleTable_Include_CustomerOrders()
    {
        return _singleContext.Customers
            .Include(c => c.Orders)
            .Where(c => c.Region == "US")
            .Take(100)
            .ToList();
    }

    /// <summary>
    /// Indexed: Include orders for customers with indexes.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("SimpleInclude")]
    public List<Customer> Indexed_Include_CustomerOrders()
    {
        return _indexedContext.Customers
            .Include(c => c.Orders)
            .Where(c => c.Region == "US")
            .Take(100)
            .ToList();
    }

    /// <summary>
    /// Sharded: Include orders for customers (co-located by region).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("SimpleInclude")]
    public List<ShardedCustomer> Sharded_Include_CustomerOrders()
    {
        return _shardedContext.Customers
            .Include(c => c.Orders)
            .Where(c => c.Region == "US")
            .Take(100)
            .ToList();
    }

    #endregion

    #region Multi-Level Include (Deep Navigation)

    /// <summary>
    /// Single table: Include orders and order items.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("MultiLevelInclude")]
    public List<Customer> SingleTable_Include_CustomerOrdersItems()
    {
        return _singleContext.Customers
            .Include(c => c.Orders)
                .ThenInclude(o => o.Items)
            .Where(c => c.Region == "EU")
            .Take(50)
            .ToList();
    }

    /// <summary>
    /// Indexed: Multi-level include with indexes.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("MultiLevelInclude")]
    public List<Customer> Indexed_Include_CustomerOrdersItems()
    {
        return _indexedContext.Customers
            .Include(c => c.Orders)
                .ThenInclude(o => o.Items)
            .Where(c => c.Region == "EU")
            .Take(50)
            .ToList();
    }

    /// <summary>
    /// Sharded: Multi-level include (all co-located by region).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("MultiLevelInclude")]
    public List<ShardedCustomer> Sharded_Include_CustomerOrdersItems()
    {
        return _shardedContext.Customers
            .Include(c => c.Orders)
                .ThenInclude(o => o.Items)
            .Where(c => c.Region == "EU")
            .Take(50)
            .ToList();
    }

    #endregion

    #region Explicit JOIN

    /// <summary>
    /// Single table: Explicit join between customers and orders.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ExplicitJoin")]
    public List<(string CustomerName, string OrderNumber, decimal Total)> SingleTable_Join_CustomerOrders()
    {
        var result = from c in _singleContext.Customers
                     join o in _singleContext.Orders on c.Id equals o.CustomerId
                     where c.Region == "US" && o.Status == OrderStatus.Delivered
                     select new { c.Name, o.OrderNumber, o.TotalAmount };

        return result.Take(500).ToList()
            .Select(x => (x.Name, x.OrderNumber, x.TotalAmount))
            .ToList();
    }

    /// <summary>
    /// Indexed: Explicit join with indexed foreign keys.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ExplicitJoin")]
    public List<(string CustomerName, string OrderNumber, decimal Total)> Indexed_Join_CustomerOrders()
    {
        var result = from c in _indexedContext.Customers
                     join o in _indexedContext.Orders on c.Id equals o.CustomerId
                     where c.Region == "US" && o.Status == OrderStatus.Delivered
                     select new { c.Name, o.OrderNumber, o.TotalAmount };

        return result.Take(500).ToList()
            .Select(x => (x.Name, x.OrderNumber, x.TotalAmount))
            .ToList();
    }

    /// <summary>
    /// Sharded: Join within same region shard.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ExplicitJoin")]
    public List<(string CustomerName, string OrderNumber, decimal Total)> Sharded_Join_CustomerOrders()
    {
        var result = from c in _shardedContext.Customers
                     join o in _shardedContext.Orders on c.Id equals o.CustomerId
                     where c.Region == "US" && o.Status == OrderStatus.Delivered
                     select new { c.Name, o.OrderNumber, o.TotalAmount };

        return result.Take(500).ToList()
            .Select(x => (x.Name, x.OrderNumber, x.TotalAmount))
            .ToList();
    }

    #endregion

    #region Multiple Joins

    /// <summary>
    /// Single table: Join across customers, orders, and items.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("MultipleJoins")]
    public List<(string Customer, string Order, string Product, int Quantity)> SingleTable_MultiJoin()
    {
        var result = from c in _singleContext.Customers
                     join o in _singleContext.Orders on c.Id equals o.CustomerId
                     join i in _singleContext.OrderItems on o.Id equals i.OrderId
                     where c.Region == "APAC"
                     select new { c.Name, o.OrderNumber, i.ProductName, i.Quantity };

        return result.Take(1000).ToList()
            .Select(x => (x.Name, x.OrderNumber, x.ProductName, x.Quantity))
            .ToList();
    }

    /// <summary>
    /// Indexed: Multiple joins with indexed foreign keys.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("MultipleJoins")]
    public List<(string Customer, string Order, string Product, int Quantity)> Indexed_MultiJoin()
    {
        var result = from c in _indexedContext.Customers
                     join o in _indexedContext.Orders on c.Id equals o.CustomerId
                     join i in _indexedContext.OrderItems on o.Id equals i.OrderId
                     where c.Region == "APAC"
                     select new { c.Name, o.OrderNumber, i.ProductName, i.Quantity };

        return result.Take(1000).ToList()
            .Select(x => (x.Name, x.OrderNumber, x.ProductName, x.Quantity))
            .ToList();
    }

    /// <summary>
    /// Sharded: Multiple joins within same region shard.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("MultipleJoins")]
    public List<(string Customer, string Order, string Product, int Quantity)> Sharded_MultiJoin()
    {
        var result = from c in _shardedContext.Customers
                     join o in _shardedContext.Orders on c.Id equals o.CustomerId
                     join i in _shardedContext.OrderItems on o.Id equals i.OrderId
                     where c.Region == "APAC"
                     select new { c.Name, o.OrderNumber, i.ProductName, i.Quantity };

        return result.Take(1000).ToList()
            .Select(x => (x.Name, x.OrderNumber, x.ProductName, x.Quantity))
            .ToList();
    }

    #endregion

    #region Join with Aggregation

    /// <summary>
    /// Single table: Join with GroupBy and Sum.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("JoinWithAggregation")]
    public List<(string Region, int OrderCount, decimal TotalRevenue)> SingleTable_JoinAggregate()
    {
        var result = from c in _singleContext.Customers
                     join o in _singleContext.Orders on c.Id equals o.CustomerId
                     group o by c.Region into g
                     select new { Region = g.Key, OrderCount = g.Count(), TotalRevenue = g.Sum(x => x.TotalAmount) };

        return result.ToList()
            .Select(x => (x.Region, x.OrderCount, x.TotalRevenue))
            .ToList();
    }

    /// <summary>
    /// Indexed: Join with aggregation using indexes.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("JoinWithAggregation")]
    public List<(string Region, int OrderCount, decimal TotalRevenue)> Indexed_JoinAggregate()
    {
        var result = from c in _indexedContext.Customers
                     join o in _indexedContext.Orders on c.Id equals o.CustomerId
                     group o by c.Region into g
                     select new { Region = g.Key, OrderCount = g.Count(), TotalRevenue = g.Sum(x => x.TotalAmount) };

        return result.ToList()
            .Select(x => (x.Region, x.OrderCount, x.TotalRevenue))
            .ToList();
    }

    /// <summary>
    /// Sharded: Join with aggregation (parallel per shard + merge).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("JoinWithAggregation")]
    public List<(string Region, int OrderCount, decimal TotalRevenue)> Sharded_JoinAggregate()
    {
        var result = from c in _shardedContext.Customers
                     join o in _shardedContext.Orders on c.Id equals o.CustomerId
                     group o by c.Region into g
                     select new { Region = g.Key, OrderCount = g.Count(), TotalRevenue = g.Sum(x => x.TotalAmount) };

        return result.ToList()
            .Select(x => (x.Region, x.OrderCount, x.TotalRevenue))
            .ToList();
    }

    #endregion

    #region Left Join Scenarios

    /// <summary>
    /// Single table: Left join to find customers without orders.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("LeftJoin")]
    public List<Customer> SingleTable_LeftJoin_CustomersWithoutOrders()
    {
        var result = from c in _singleContext.Customers
                     join o in _singleContext.Orders on c.Id equals o.CustomerId into orders
                     from o in orders.DefaultIfEmpty()
                     where o == null
                     select c;

        return result.Take(100).ToList();
    }

    /// <summary>
    /// Indexed: Left join with indexes.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("LeftJoin")]
    public List<Customer> Indexed_LeftJoin_CustomersWithoutOrders()
    {
        var result = from c in _indexedContext.Customers
                     join o in _indexedContext.Orders on c.Id equals o.CustomerId into orders
                     from o in orders.DefaultIfEmpty()
                     where o == null
                     select c;

        return result.Take(100).ToList();
    }

    /// <summary>
    /// Sharded: Left join across shards.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("LeftJoin")]
    public List<ShardedCustomer> Sharded_LeftJoin_CustomersWithoutOrders()
    {
        var result = from c in _shardedContext.Customers
                     join o in _shardedContext.Orders on c.Id equals o.CustomerId into orders
                     from o in orders.DefaultIfEmpty()
                     where o == null
                     select c;

        return result.Take(100).ToList();
    }

    #endregion

    #region Filtered Include

    /// <summary>
    /// Single table: Include with filter on related entity.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("FilteredInclude")]
    public List<Customer> SingleTable_FilteredInclude()
    {
        return _singleContext.Customers
            .Include(c => c.Orders.Where(o => o.Status == OrderStatus.Delivered))
            .Where(c => c.Tier == CustomerTier.Gold)
            .Take(50)
            .ToList();
    }

    /// <summary>
    /// Indexed: Filtered include with indexes.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("FilteredInclude")]
    public List<Customer> Indexed_FilteredInclude()
    {
        return _indexedContext.Customers
            .Include(c => c.Orders.Where(o => o.Status == OrderStatus.Delivered))
            .Where(c => c.Tier == CustomerTier.Gold)
            .Take(50)
            .ToList();
    }

    /// <summary>
    /// Sharded: Filtered include on sharded entities.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("FilteredInclude")]
    public List<ShardedCustomer> Sharded_FilteredInclude()
    {
        return _shardedContext.Customers
            .Include(c => c.Orders.Where(o => o.Status == OrderStatus.Delivered))
            .Where(c => c.Tier == CustomerTier.Gold)
            .Take(50)
            .ToList();
    }

    #endregion
}
