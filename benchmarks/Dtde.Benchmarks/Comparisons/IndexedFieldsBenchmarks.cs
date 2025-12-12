using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dtde.Benchmarks.Data;
using Dtde.Benchmarks.Entities;
using Dtde.EntityFramework.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Benchmarks.Comparisons;

/// <summary>
/// Benchmarks comparing indexed vs non-indexed field performance.
/// Demonstrates index impact on both single table and sharded scenarios.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class IndexedFieldsBenchmarks
{
    private SingleTableDbContext _nonIndexedContext = null!;
    private IndexedSingleTableDbContext _indexedContext = null!;
    private ShardedDbContext _shardedContext = null!;

    private List<Order> _orders = null!;
    private List<Customer> _customers = null!;
    private List<Transaction> _transactions = null!;

    [Params(50_000, 100_000)]
    public int RecordCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Setup non-indexed context
        var nonIndexedOptions = new DbContextOptionsBuilder<SingleTableDbContext>()
            .UseSqlite($"Data Source=nonindexed_benchmark_{RecordCount}.db")
            .Options;
        _nonIndexedContext = new SingleTableDbContext(nonIndexedOptions);
        _nonIndexedContext.Database.EnsureDeleted();
        _nonIndexedContext.Database.EnsureCreated();

        // Setup indexed context
        var indexedOptions = new DbContextOptionsBuilder<IndexedSingleTableDbContext>()
            .UseSqlite($"Data Source=indexed_benchmark_{RecordCount}.db")
            .Options;
        _indexedContext = new IndexedSingleTableDbContext(indexedOptions);
        _indexedContext.Database.EnsureDeleted();
        _indexedContext.Database.EnsureCreated();

        // Setup sharded context (always has indexes on shard keys)
        var shardedOptions = new DbContextOptionsBuilder<ShardedDbContext>()
            .UseSqlite($"Data Source=sharded_indexed_{RecordCount}.db")
            .UseDtde()
            .Options;
        _shardedContext = new ShardedDbContext(shardedOptions);
        _shardedContext.Database.EnsureDeleted();
        _shardedContext.Database.EnsureCreated();

        // Generate data
        _customers = DataGenerator.GenerateCustomers(RecordCount / 10);
        _orders = DataGenerator.GenerateOrders(RecordCount / 2, _customers);
        _transactions = DataGenerator.GenerateTransactions(RecordCount);

        SeedData();
    }

    private void SeedData()
    {
        const int batchSize = 10000;

        // Seed to non-indexed
        for (var i = 0; i < _customers.Count; i += batchSize)
        {
            var batch = _customers.Skip(i).Take(batchSize).ToList();
            _nonIndexedContext.Customers.AddRange(batch);
            _nonIndexedContext.SaveChanges();
        }

        // Seed to indexed (clone customers with new IDs)
        var indexedCustomers = _customers.Select(c => new Customer
        {
            Name = c.Name,
            Email = c.Email + ".idx",
            Region = c.Region,
            Phone = c.Phone,
            Address = c.Address,
            City = c.City,
            Country = c.Country,
            Tier = c.Tier,
            CreatedAt = c.CreatedAt,
            IsActive = c.IsActive
        }).ToList();

        for (var i = 0; i < indexedCustomers.Count; i += batchSize)
        {
            var batch = indexedCustomers.Skip(i).Take(batchSize).ToList();
            _indexedContext.Customers.AddRange(batch);
            _indexedContext.SaveChanges();
        }

        // Seed orders
        for (var i = 0; i < _orders.Count; i += batchSize)
        {
            var batch = _orders.Skip(i).Take(batchSize).ToList();
            _nonIndexedContext.Orders.AddRange(batch);
            _nonIndexedContext.SaveChanges();
        }

        var indexedOrders = _orders.Select(o => new Order
        {
            OrderNumber = o.OrderNumber + "-idx",
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

        for (var i = 0; i < indexedOrders.Count; i += batchSize)
        {
            var batch = indexedOrders.Skip(i).Take(batchSize).ToList();
            _indexedContext.Orders.AddRange(batch);
            _indexedContext.SaveChanges();
        }

        // Seed transactions
        for (var i = 0; i < _transactions.Count; i += batchSize)
        {
            var batch = _transactions.Skip(i).Take(batchSize).ToList();
            _nonIndexedContext.Transactions.AddRange(batch);
            _nonIndexedContext.SaveChanges();
        }

        var indexedTransactions = _transactions.Select(t => new Transaction
        {
            TransactionRef = t.TransactionRef + "-idx",
            AccountNumber = t.AccountNumber,
            TransactionDate = t.TransactionDate,
            Amount = t.Amount,
            Type = t.Type,
            Description = t.Description,
            Category = t.Category,
            Merchant = t.Merchant,
            BalanceBefore = t.BalanceBefore,
            BalanceAfter = t.BalanceAfter,
            Status = t.Status,
            CreatedAt = t.CreatedAt
        }).ToList();

        for (var i = 0; i < indexedTransactions.Count; i += batchSize)
        {
            var batch = indexedTransactions.Skip(i).Take(batchSize).ToList();
            _indexedContext.Transactions.AddRange(batch);
            _indexedContext.SaveChanges();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _nonIndexedContext?.Dispose();
        _indexedContext?.Dispose();
        _shardedContext?.Dispose();

        // Clear SQLite connection pool to release file handles
        SqliteConnection.ClearAllPools();
        Thread.Sleep(100);

        try { File.Delete($"nonindexed_benchmark_{RecordCount}.db"); } catch { /* ignore */ }
        try { File.Delete($"indexed_benchmark_{RecordCount}.db"); } catch { /* ignore */ }
        try { File.Delete($"sharded_indexed_{RecordCount}.db"); } catch { /* ignore */ }
    }

    #region WHERE Clause on Non-Indexed Field

    /// <summary>
    /// Non-indexed: Filter by non-indexed field (full table scan).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("NonIndexedFilter")]
    public List<Customer> NonIndexed_FilterByTier()
    {
        return _nonIndexedContext.Customers
            .Where(c => c.Tier == CustomerTier.Gold)
            .ToList();
    }

    /// <summary>
    /// Indexed: Filter by indexed field (index seek).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("IndexedFilter")]
    public List<Customer> Indexed_FilterByRegion()
    {
        return _indexedContext.Customers
            .Where(c => c.Region == "US")
            .ToList();
    }

    /// <summary>
    /// Non-indexed: Filter by non-indexed field.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("NonIndexedFilter")]
    public List<Customer> NonIndexed_FilterByCity()
    {
        return _nonIndexedContext.Customers
            .Where(c => c.City == "New York")
            .ToList();
    }

    #endregion

    #region Range Queries

    /// <summary>
    /// Non-indexed: Date range query without index.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("DateRangeQuery")]
    public List<Order> NonIndexed_DateRange()
    {
        var startDate = DateTime.Now.AddMonths(-3);
        var endDate = DateTime.Now;

        return _nonIndexedContext.Orders
            .Where(o => o.OrderDate >= startDate && o.OrderDate <= endDate)
            .ToList();
    }

    /// <summary>
    /// Indexed: Date range query with index.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("DateRangeQuery")]
    public List<Order> Indexed_DateRange()
    {
        var startDate = DateTime.Now.AddMonths(-3);
        var endDate = DateTime.Now;

        return _indexedContext.Orders
            .Where(o => o.OrderDate >= startDate && o.OrderDate <= endDate)
            .ToList();
    }

    #endregion

    #region Compound Index Queries

    /// <summary>
    /// Non-indexed: Query with multiple filter conditions.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("CompoundQuery")]
    public List<Order> NonIndexed_CompoundFilter()
    {
        var startDate = DateTime.Now.AddMonths(-6);

        return _nonIndexedContext.Orders
            .Where(o => o.Region == "US" && o.OrderDate >= startDate && o.Status == OrderStatus.Delivered)
            .ToList();
    }

    /// <summary>
    /// Indexed: Query with compound index (Region + OrderDate).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("CompoundQuery")]
    public List<Order> Indexed_CompoundFilter()
    {
        var startDate = DateTime.Now.AddMonths(-6);

        return _indexedContext.Orders
            .Where(o => o.Region == "US" && o.OrderDate >= startDate && o.Status == OrderStatus.Delivered)
            .ToList();
    }

    #endregion

    #region Unique Index Lookups

    /// <summary>
    /// Non-indexed: Find by unique field without index.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("UniqueLookup")]
    public Customer? NonIndexed_FindByEmail()
    {
        var email = _customers[RecordCount / 20].Email;
        return _nonIndexedContext.Customers
            .FirstOrDefault(c => c.Email == email);
    }

    /// <summary>
    /// Indexed: Find by unique field with unique index.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("UniqueLookup")]
    public Customer? Indexed_FindByEmail()
    {
        var email = _customers[RecordCount / 20].Email + ".idx";
        return _indexedContext.Customers
            .FirstOrDefault(c => c.Email == email);
    }

    /// <summary>
    /// Non-indexed: Find by transaction reference without index.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("UniqueLookup")]
    public Transaction? NonIndexed_FindByTransactionRef()
    {
        var txRef = _transactions[RecordCount / 2].TransactionRef;
        return _nonIndexedContext.Transactions
            .FirstOrDefault(t => t.TransactionRef == txRef);
    }

    /// <summary>
    /// Indexed: Find by transaction reference with unique index.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("UniqueLookup")]
    public Transaction? Indexed_FindByTransactionRef()
    {
        var txRef = _transactions[RecordCount / 2].TransactionRef + "-idx";
        return _indexedContext.Transactions
            .FirstOrDefault(t => t.TransactionRef == txRef);
    }

    #endregion

    #region OrderBy Performance

    /// <summary>
    /// Non-indexed: OrderBy on non-indexed column.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("OrderBy")]
    public List<Transaction> NonIndexed_OrderBy_Amount()
    {
        return _nonIndexedContext.Transactions
            .OrderByDescending(t => t.Amount)
            .Take(100)
            .ToList();
    }

    /// <summary>
    /// Indexed: OrderBy on indexed column.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("OrderBy")]
    public List<Transaction> Indexed_OrderBy_TransactionDate()
    {
        return _indexedContext.Transactions
            .OrderByDescending(t => t.TransactionDate)
            .Take(100)
            .ToList();
    }

    #endregion

    #region Aggregation with Index

    /// <summary>
    /// Non-indexed: Count with filter on non-indexed field.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("AggregationWithFilter")]
    public int NonIndexed_Count_WithFilter()
    {
        return _nonIndexedContext.Transactions
            .Count(t => t.Category == "Income");
    }

    /// <summary>
    /// Indexed: Count with filter on indexed field.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("AggregationWithFilter")]
    public int Indexed_Count_WithFilter()
    {
        return _indexedContext.Transactions
            .Count(t => t.AccountNumber == "ACC-50000");
    }

    /// <summary>
    /// Non-indexed: Sum with filter on non-indexed field.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("AggregationWithFilter")]
    public decimal NonIndexed_Sum_WithFilter()
    {
        var startDate = DateTime.Now.AddMonths(-6);
        return _nonIndexedContext.Transactions
            .Where(t => t.Category == "Expense" && t.TransactionDate >= startDate)
            .Sum(t => t.Amount);
    }

    /// <summary>
    /// Indexed: Sum with filter on indexed field.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("AggregationWithFilter")]
    public decimal Indexed_Sum_WithFilter()
    {
        var startDate = DateTime.Now.AddMonths(-6);
        return _indexedContext.Transactions
            .Where(t => t.TransactionDate >= startDate && t.Type == TransactionType.Debit)
            .Sum(t => t.Amount);
    }

    #endregion
}
