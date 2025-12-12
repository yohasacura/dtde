using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dtde.Benchmarks.Data;
using Dtde.Benchmarks.Entities;
using Dtde.EntityFramework.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Benchmarks.Comparisons;

/// <summary>
/// Benchmarks for write operations: Insert, Update, Delete.
/// Compares single table vs sharded approaches for data modification.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class WriteOperationsBenchmarks
{
    private SingleTableDbContext _singleContext = null!;
    private IndexedSingleTableDbContext _indexedContext = null!;
    private ShardedDbContext _shardedContext = null!;

    private List<Customer> _customersToInsert = null!;
    private List<ShardedCustomer> _shardedCustomersToInsert = null!;

    [Params(100, 1000, 5000)]
    public int BatchSize { get; set; }

    private int _iterationCount;

    [GlobalSetup]
    public void Setup()
    {
        _iterationCount = 0;

        // Create contexts
        var singleOptions = new DbContextOptionsBuilder<SingleTableDbContext>()
            .UseSqlite($"Data Source=write_single_{BatchSize}.db")
            .Options;
        _singleContext = new SingleTableDbContext(singleOptions);
        _singleContext.Database.EnsureDeleted();
        _singleContext.Database.EnsureCreated();

        var indexedOptions = new DbContextOptionsBuilder<IndexedSingleTableDbContext>()
            .UseSqlite($"Data Source=write_indexed_{BatchSize}.db")
            .Options;
        _indexedContext = new IndexedSingleTableDbContext(indexedOptions);
        _indexedContext.Database.EnsureDeleted();
        _indexedContext.Database.EnsureCreated();

        var shardedOptions = new DbContextOptionsBuilder<ShardedDbContext>()
            .UseSqlite($"Data Source=write_sharded_{BatchSize}.db")
            .UseDtde()
            .Options;
        _shardedContext = new ShardedDbContext(shardedOptions);
        _shardedContext.Database.EnsureDeleted();
        _shardedContext.Database.EnsureCreated();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _iterationCount++;

        // Generate fresh data for each iteration
        _customersToInsert = DataGenerator.GenerateCustomers(BatchSize);
        _shardedCustomersToInsert = DataGenerator.GenerateShardedCustomers(BatchSize);

        // Make emails unique per iteration
        foreach (var c in _customersToInsert)
        {
            c.Email = $"iter{_iterationCount}_{c.Email}";
        }

        foreach (var c in _shardedCustomersToInsert)
        {
            c.Email = $"iter{_iterationCount}_{c.Email}";
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

        try { File.Delete($"write_single_{BatchSize}.db"); } catch { /* ignore */ }
        try { File.Delete($"write_indexed_{BatchSize}.db"); } catch { /* ignore */ }
        try { File.Delete($"write_sharded_{BatchSize}.db"); } catch { /* ignore */ }
    }

    #region Batch Insert

    /// <summary>
    /// Single table: Batch insert customers.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("BatchInsert")]
    public int SingleTable_BatchInsert()
    {
        _singleContext.Customers.AddRange(_customersToInsert);
        return _singleContext.SaveChanges();
    }

    /// <summary>
    /// Indexed: Batch insert with indexed table (index maintenance overhead).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("BatchInsert")]
    public int Indexed_BatchInsert()
    {
        var cloned = _customersToInsert.Select(c => new Customer
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

        _indexedContext.Customers.AddRange(cloned);
        return _indexedContext.SaveChanges();
    }

    /// <summary>
    /// Sharded: Batch insert into sharded tables.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("BatchInsert")]
    public int Sharded_BatchInsert()
    {
        _shardedContext.Customers.AddRange(_shardedCustomersToInsert);
        return _shardedContext.SaveChanges();
    }

    #endregion

    #region Single Insert

    /// <summary>
    /// Single table: Insert one customer at a time.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("SingleInsert")]
    public int SingleTable_SingleInsert()
    {
        var count = 0;
        foreach (var customer in _customersToInsert.Take(10))
        {
            customer.Email += ".single";
            _singleContext.Customers.Add(customer);
            count += _singleContext.SaveChanges();
        }

        return count;
    }

    /// <summary>
    /// Indexed: Single insert with index maintenance.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("SingleInsert")]
    public int Indexed_SingleInsert()
    {
        var count = 0;
        foreach (var customer in _customersToInsert.Take(10))
        {
            var clone = new Customer
            {
                Name = customer.Name,
                Email = customer.Email + ".single.idx",
                Region = customer.Region,
                Phone = customer.Phone,
                Address = customer.Address,
                City = customer.City,
                Country = customer.Country,
                Tier = customer.Tier,
                CreatedAt = customer.CreatedAt,
                IsActive = customer.IsActive
            };
            _indexedContext.Customers.Add(clone);
            count += _indexedContext.SaveChanges();
        }

        return count;
    }

    /// <summary>
    /// Sharded: Single insert routing to correct shard.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("SingleInsert")]
    public int Sharded_SingleInsert()
    {
        var count = 0;
        foreach (var customer in _shardedCustomersToInsert.Take(10))
        {
            customer.Email += ".single";
            _shardedContext.Customers.Add(customer);
            count += _shardedContext.SaveChanges();
        }

        return count;
    }

    #endregion

    #region Update Operations

    /// <summary>
    /// Single table: Update multiple records by query.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("BatchUpdate")]
    public int SingleTable_BatchUpdate()
    {
        var customers = _singleContext.Customers
            .Where(c => c.Region == "US")
            .Take(100)
            .ToList();

        foreach (var c in customers)
        {
            c.Tier = CustomerTier.Gold;
            c.IsActive = true;
        }

        return _singleContext.SaveChanges();
    }

    /// <summary>
    /// Indexed: Update with index maintenance overhead.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("BatchUpdate")]
    public int Indexed_BatchUpdate()
    {
        var customers = _indexedContext.Customers
            .Where(c => c.Region == "US")
            .Take(100)
            .ToList();

        foreach (var c in customers)
        {
            c.Tier = CustomerTier.Gold;
            c.IsActive = true;
        }

        return _indexedContext.SaveChanges();
    }

    /// <summary>
    /// Sharded: Update within a single shard.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("BatchUpdate")]
    public int Sharded_BatchUpdate()
    {
        var customers = _shardedContext.Customers
            .Where(c => c.Region == "US")
            .Take(100)
            .ToList();

        foreach (var c in customers)
        {
            c.Tier = CustomerTier.Gold;
            c.IsActive = true;
        }

        return _shardedContext.SaveChanges();
    }

    #endregion

    #region Delete Operations

    /// <summary>
    /// Single table: Delete records by query.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("BatchDelete")]
    public int SingleTable_BatchDelete()
    {
        var customers = _singleContext.Customers
            .Where(c => !c.IsActive)
            .Take(50)
            .ToList();

        _singleContext.Customers.RemoveRange(customers);
        return _singleContext.SaveChanges();
    }

    /// <summary>
    /// Indexed: Delete with index cleanup overhead.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("BatchDelete")]
    public int Indexed_BatchDelete()
    {
        var customers = _indexedContext.Customers
            .Where(c => !c.IsActive)
            .Take(50)
            .ToList();

        _indexedContext.Customers.RemoveRange(customers);
        return _indexedContext.SaveChanges();
    }

    /// <summary>
    /// Sharded: Delete from sharded tables.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("BatchDelete")]
    public int Sharded_BatchDelete()
    {
        var customers = _shardedContext.Customers
            .Where(c => !c.IsActive)
            .Take(50)
            .ToList();

        _shardedContext.Customers.RemoveRange(customers);
        return _shardedContext.SaveChanges();
    }

    #endregion

    #region Insert with Related Entities

    /// <summary>
    /// Single table: Insert customer with orders (cascading insert).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("CascadingInsert")]
    public int SingleTable_CascadingInsert()
    {
        var customers = DataGenerator.GenerateCustomers(10);
        foreach (var c in customers)
        {
            c.Email = $"cascade_{_iterationCount}_{c.Email}";
        }

        var orders = DataGenerator.GenerateOrders(50, customers);

        _singleContext.Customers.AddRange(customers);
        _singleContext.Orders.AddRange(orders);
        return _singleContext.SaveChanges();
    }

    /// <summary>
    /// Sharded: Insert sharded customer with co-located orders.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("CascadingInsert")]
    public int Sharded_CascadingInsert()
    {
        var customers = DataGenerator.GenerateShardedCustomers(10);
        foreach (var c in customers)
        {
            c.Email = $"cascade_{_iterationCount}_{c.Email}";
        }

        var orders = DataGenerator.GenerateShardedOrders(50, customers);

        _shardedContext.Customers.AddRange(customers);
        _shardedContext.Orders.AddRange(orders);
        return _shardedContext.SaveChanges();
    }

    #endregion

    #region Upsert Pattern

    /// <summary>
    /// Single table: Upsert pattern (check exists, insert or update).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Upsert")]
    public int SingleTable_Upsert()
    {
        var count = 0;
        var sampleEmails = _customersToInsert.Take(20).Select(c => c.Email + ".upsert").ToList();

        foreach (var email in sampleEmails)
        {
            var existing = _singleContext.Customers.FirstOrDefault(c => c.Email == email);
            if (existing != null)
            {
                existing.IsActive = true;
                existing.Tier = CustomerTier.Gold;
            }
            else
            {
                _singleContext.Customers.Add(new Customer
                {
                    Name = "Upsert Customer",
                    Email = email,
                    Region = "US",
                    Tier = CustomerTier.Standard,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                });
            }

            count += _singleContext.SaveChanges();
        }

        return count;
    }

    /// <summary>
    /// Sharded: Upsert pattern with shard-aware lookup.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Upsert")]
    public int Sharded_Upsert()
    {
        var count = 0;
        var sampleEmails = _shardedCustomersToInsert.Take(20).Select(c => c.Email + ".upsert").ToList();
        var regions = new[] { "US", "EU", "APAC" };
        var random = new Random(42);

        foreach (var email in sampleEmails)
        {
            var region = regions[random.Next(regions.Length)];
            var existing = _shardedContext.Customers
                .FirstOrDefault(c => c.Email == email && c.Region == region);

            if (existing != null)
            {
                existing.IsActive = true;
                existing.Tier = CustomerTier.Gold;
            }
            else
            {
                _shardedContext.Customers.Add(new ShardedCustomer
                {
                    Name = "Upsert Customer",
                    Email = email,
                    Region = region,
                    Tier = CustomerTier.Standard,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                });
            }

            count += _shardedContext.SaveChanges();
        }

        return count;
    }

    #endregion

    #region Bulk EF Core Operations

    /// <summary>
    /// Single table: EF Core bulk update (ExecuteUpdate).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ExecuteUpdate")]
    public int SingleTable_ExecuteUpdate()
    {
        return _singleContext.Customers
            .Where(c => c.Tier == CustomerTier.Bronze)
            .ExecuteUpdate(s => s
                .SetProperty(c => c.Tier, CustomerTier.Standard)
                .SetProperty(c => c.IsActive, true));
    }

    /// <summary>
    /// Indexed: ExecuteUpdate with index maintenance.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ExecuteUpdate")]
    public int Indexed_ExecuteUpdate()
    {
        return _indexedContext.Customers
            .Where(c => c.Tier == CustomerTier.Bronze)
            .ExecuteUpdate(s => s
                .SetProperty(c => c.Tier, CustomerTier.Standard)
                .SetProperty(c => c.IsActive, true));
    }

    /// <summary>
    /// Sharded: ExecuteUpdate across shards.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ExecuteUpdate")]
    public int Sharded_ExecuteUpdate()
    {
        return _shardedContext.Customers
            .Where(c => c.Tier == CustomerTier.Bronze)
            .ExecuteUpdate(s => s
                .SetProperty(c => c.Tier, CustomerTier.Standard)
                .SetProperty(c => c.IsActive, true));
    }

    /// <summary>
    /// Single table: EF Core bulk delete (ExecuteDelete).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ExecuteDelete")]
    public int SingleTable_ExecuteDelete()
    {
        return _singleContext.Customers
            .Where(c => c.Name.StartsWith("DeleteMe"))
            .ExecuteDelete();
    }

    /// <summary>
    /// Indexed: ExecuteDelete with index cleanup.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ExecuteDelete")]
    public int Indexed_ExecuteDelete()
    {
        return _indexedContext.Customers
            .Where(c => c.Name.StartsWith("DeleteMe"))
            .ExecuteDelete();
    }

    /// <summary>
    /// Sharded: ExecuteDelete across shards.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ExecuteDelete")]
    public int Sharded_ExecuteDelete()
    {
        return _shardedContext.Customers
            .Where(c => c.Name.StartsWith("DeleteMe"))
            .ExecuteDelete();
    }

    #endregion
}
