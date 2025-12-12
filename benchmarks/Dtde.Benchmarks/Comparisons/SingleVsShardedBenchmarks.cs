using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dtde.Benchmarks.Data;
using Dtde.Benchmarks.Entities;
using Dtde.EntityFramework.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Benchmarks.Comparisons;

/// <summary>
/// Benchmarks comparing single large table vs sharded tables.
/// This is the core comparison showing the performance benefits of sharding.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class SingleVsShardedBenchmarks
{
    private SingleTableDbContext _singleContext = null!;
    private ShardedDbContext _shardedContext = null!;

    private List<Customer> _customers = null!;
    private List<ShardedCustomer> _shardedCustomers = null!;
    private List<Order> _orders = null!;
    private List<ShardedOrder> _shardedOrders = null!;
    private List<Transaction> _transactions = null!;
    private List<ShardedTransaction> _shardedTransactions = null!;

    [Params(10_000, 50_000, 100_000)]
    public int RecordCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Setup single table context
        var singleOptions = new DbContextOptionsBuilder<SingleTableDbContext>()
            .UseSqlite($"Data Source=single_benchmark_{RecordCount}.db")
            .Options;
        _singleContext = new SingleTableDbContext(singleOptions);
        _singleContext.Database.EnsureDeleted();
        _singleContext.Database.EnsureCreated();

        // Setup sharded context
        var shardedOptions = new DbContextOptionsBuilder<ShardedDbContext>()
            .UseSqlite($"Data Source=sharded_benchmark_{RecordCount}.db")
            .UseDtde()
            .Options;
        _shardedContext = new ShardedDbContext(shardedOptions);
        _shardedContext.Database.EnsureDeleted();
        _shardedContext.Database.EnsureCreated();

        // Generate data
        _customers = DataGenerator.GenerateCustomers(RecordCount / 10);
        _shardedCustomers = DataGenerator.GenerateShardedCustomers(RecordCount / 10);
        _orders = DataGenerator.GenerateOrders(RecordCount / 5, _customers);
        _shardedOrders = DataGenerator.GenerateShardedOrders(RecordCount / 5, _shardedCustomers);
        _transactions = DataGenerator.GenerateTransactions(RecordCount);
        _shardedTransactions = DataGenerator.GenerateShardedTransactions(RecordCount);

        // Seed data in batches
        SeedData();
    }

    private void SeedData()
    {
        const int batchSize = 10000;

        // Seed customers
        for (var i = 0; i < _customers.Count; i += batchSize)
        {
            var batch = _customers.Skip(i).Take(batchSize).ToList();
            _singleContext.Customers.AddRange(batch);
            _singleContext.SaveChanges();
        }

        for (var i = 0; i < _shardedCustomers.Count; i += batchSize)
        {
            var batch = _shardedCustomers.Skip(i).Take(batchSize).ToList();
            _shardedContext.Customers.AddRange(batch);
            _shardedContext.SaveChanges();
        }

        // Seed orders
        for (var i = 0; i < _orders.Count; i += batchSize)
        {
            var batch = _orders.Skip(i).Take(batchSize).ToList();
            _singleContext.Orders.AddRange(batch);
            _singleContext.SaveChanges();
        }

        for (var i = 0; i < _shardedOrders.Count; i += batchSize)
        {
            var batch = _shardedOrders.Skip(i).Take(batchSize).ToList();
            _shardedContext.Orders.AddRange(batch);
            _shardedContext.SaveChanges();
        }

        // Seed transactions
        for (var i = 0; i < _transactions.Count; i += batchSize)
        {
            var batch = _transactions.Skip(i).Take(batchSize).ToList();
            _singleContext.Transactions.AddRange(batch);
            _singleContext.SaveChanges();
        }

        for (var i = 0; i < _shardedTransactions.Count; i += batchSize)
        {
            var batch = _shardedTransactions.Skip(i).Take(batchSize).ToList();
            _shardedContext.Transactions.AddRange(batch);
            _shardedContext.SaveChanges();
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

        // Clean up database files
        try { File.Delete($"single_benchmark_{RecordCount}.db"); } catch { /* ignore */ }
        try { File.Delete($"sharded_benchmark_{RecordCount}.db"); } catch { /* ignore */ }
    }

    #region Point Lookups

    /// <summary>
    /// Single table: Point lookup by primary key.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("PointLookup")]
    public Customer? SingleTable_PointLookup_ById()
    {
        var id = new Random().Next(1, _customers.Count);
        return _singleContext.Customers.Find(id);
    }

    /// <summary>
    /// Sharded: Point lookup by primary key (requires fan-out without shard key).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("PointLookup")]
    public ShardedCustomer? Sharded_PointLookup_ById()
    {
        var id = new Random().Next(1, _shardedCustomers.Count);
        return _shardedContext.Customers.Find(id);
    }

    /// <summary>
    /// Single table: Point lookup with shard key in WHERE clause.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("PointLookup")]
    public Customer? SingleTable_PointLookup_WithRegion()
    {
        var id = new Random().Next(1, _customers.Count);
        var region = _customers[id - 1].Region;
        return _singleContext.Customers
            .Where(c => c.Region == region && c.Id == id)
            .FirstOrDefault();
    }

    /// <summary>
    /// Sharded: Point lookup with shard key (single shard access).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("PointLookup")]
    public ShardedCustomer? Sharded_PointLookup_WithRegion()
    {
        var id = new Random().Next(1, _shardedCustomers.Count);
        var region = _shardedCustomers[id - 1].Region;
        return _shardedContext.Customers
            .Where(c => c.Region == region && c.Id == id)
            .FirstOrDefault();
    }

    #endregion

    #region Range Scans

    /// <summary>
    /// Single table: Range scan across all regions.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("RangeScan")]
    public List<Order> SingleTable_RangeScan_AllRegions()
    {
        var startDate = DateTime.Now.AddMonths(-6);
        var endDate = DateTime.Now;

        return _singleContext.Orders
            .Where(o => o.OrderDate >= startDate && o.OrderDate <= endDate)
            .OrderByDescending(o => o.OrderDate)
            .Take(1000)
            .ToList();
    }

    /// <summary>
    /// Sharded: Range scan across all regions (fan-out).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("RangeScan")]
    public List<ShardedOrder> Sharded_RangeScan_AllRegions()
    {
        var startDate = DateTime.Now.AddMonths(-6);
        var endDate = DateTime.Now;

        return _shardedContext.Orders
            .Where(o => o.OrderDate >= startDate && o.OrderDate <= endDate)
            .OrderByDescending(o => o.OrderDate)
            .Take(1000)
            .ToList();
    }

    /// <summary>
    /// Single table: Range scan for specific region.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("RangeScan")]
    public List<Order> SingleTable_RangeScan_SingleRegion()
    {
        var startDate = DateTime.Now.AddMonths(-6);
        var endDate = DateTime.Now;

        return _singleContext.Orders
            .Where(o => o.Region == "US" && o.OrderDate >= startDate && o.OrderDate <= endDate)
            .OrderByDescending(o => o.OrderDate)
            .Take(1000)
            .ToList();
    }

    /// <summary>
    /// Sharded: Range scan for specific region (single shard).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("RangeScan")]
    public List<ShardedOrder> Sharded_RangeScan_SingleRegion()
    {
        var startDate = DateTime.Now.AddMonths(-6);
        var endDate = DateTime.Now;

        return _shardedContext.Orders
            .Where(o => o.Region == "US" && o.OrderDate >= startDate && o.OrderDate <= endDate)
            .OrderByDescending(o => o.OrderDate)
            .Take(1000)
            .ToList();
    }

    #endregion

    #region Aggregations

    /// <summary>
    /// Single table: Aggregation across all data.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Aggregation")]
    public decimal SingleTable_Aggregation_TotalAmount()
    {
        return _singleContext.Orders.Sum(o => o.TotalAmount);
    }

    /// <summary>
    /// Sharded: Aggregation across all shards (fan-out + merge).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Aggregation")]
    public decimal Sharded_Aggregation_TotalAmount()
    {
        return _shardedContext.Orders.Sum(o => o.TotalAmount);
    }

    /// <summary>
    /// Single table: GroupBy aggregation.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Aggregation")]
    public List<(string Region, decimal Total)> SingleTable_Aggregation_GroupByRegion()
    {
        return _singleContext.Orders
            .GroupBy(o => o.Region)
            .Select(g => new { Region = g.Key, Total = g.Sum(o => o.TotalAmount) })
            .ToList()
            .Select(x => (x.Region, x.Total))
            .ToList();
    }

    /// <summary>
    /// Sharded: GroupBy aggregation (parallel per-shard + merge).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Aggregation")]
    public List<(string Region, decimal Total)> Sharded_Aggregation_GroupByRegion()
    {
        return _shardedContext.Orders
            .GroupBy(o => o.Region)
            .Select(g => new { Region = g.Key, Total = g.Sum(o => o.TotalAmount) })
            .ToList()
            .Select(x => (x.Region, x.Total))
            .ToList();
    }

    #endregion

    #region Date-Based Queries (Transaction Sharding)

    /// <summary>
    /// Single table: Query transactions for a specific month.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("DateQuery")]
    public List<Transaction> SingleTable_DateQuery_SingleMonth()
    {
        var startDate = new DateTime(2024, 6, 1);
        var endDate = new DateTime(2024, 6, 30);

        return _singleContext.Transactions
            .Where(t => t.TransactionDate >= startDate && t.TransactionDate <= endDate)
            .OrderByDescending(t => t.TransactionDate)
            .Take(1000)
            .ToList();
    }

    /// <summary>
    /// Sharded: Query transactions for a specific month (single shard).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("DateQuery")]
    public List<ShardedTransaction> Sharded_DateQuery_SingleMonth()
    {
        var startDate = new DateTime(2024, 6, 1);
        var endDate = new DateTime(2024, 6, 30);

        return _shardedContext.Transactions
            .Where(t => t.TransactionDate >= startDate && t.TransactionDate <= endDate)
            .OrderByDescending(t => t.TransactionDate)
            .Take(1000)
            .ToList();
    }

    /// <summary>
    /// Single table: Query transactions across multiple months.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("DateQuery")]
    public List<Transaction> SingleTable_DateQuery_MultipleMonths()
    {
        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 12, 31);

        return _singleContext.Transactions
            .Where(t => t.TransactionDate >= startDate && t.TransactionDate <= endDate)
            .OrderByDescending(t => t.TransactionDate)
            .Take(5000)
            .ToList();
    }

    /// <summary>
    /// Sharded: Query transactions across multiple months (multiple shards).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("DateQuery")]
    public List<ShardedTransaction> Sharded_DateQuery_MultipleMonths()
    {
        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 12, 31);

        return _shardedContext.Transactions
            .Where(t => t.TransactionDate >= startDate && t.TransactionDate <= endDate)
            .OrderByDescending(t => t.TransactionDate)
            .Take(5000)
            .ToList();
    }

    #endregion

    #region Count Operations

    /// <summary>
    /// Single table: Count all records.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Count")]
    public int SingleTable_Count_All()
    {
        return _singleContext.Transactions.Count();
    }

    /// <summary>
    /// Sharded: Count all records (fan-out + sum).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Count")]
    public int Sharded_Count_All()
    {
        return _shardedContext.Transactions.Count();
    }

    /// <summary>
    /// Single table: Count with filter.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Count")]
    public int SingleTable_Count_Filtered()
    {
        return _singleContext.Transactions
            .Count(t => t.Type == TransactionType.Credit && t.Amount > 1000);
    }

    /// <summary>
    /// Sharded: Count with filter (parallel count + sum).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Count")]
    public int Sharded_Count_Filtered()
    {
        return _shardedContext.Transactions
            .Count(t => t.Type == TransactionType.Credit && t.Amount > 1000);
    }

    #endregion
}
