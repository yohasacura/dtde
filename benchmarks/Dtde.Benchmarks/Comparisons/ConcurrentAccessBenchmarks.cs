using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dtde.Benchmarks.Data;
using Dtde.Benchmarks.Entities;
using Dtde.EntityFramework.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace Dtde.Benchmarks.Comparisons;

/// <summary>
/// Benchmarks for concurrent access patterns comparing single table vs sharded approaches.
/// Tests how sharding affects parallel read/write performance and contention.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class ConcurrentAccessBenchmarks
{
    private DbContextOptions<SingleTableDbContext> _singleOptions = null!;
    private DbContextOptions<IndexedSingleTableDbContext> _indexedOptions = null!;
    private DbContextOptions<ShardedDbContext> _shardedOptions = null!;

    private List<Customer> _customers = null!;
    private List<ShardedCustomer> _shardedCustomers = null!;
    private readonly string[] _regions = ["US", "EU", "APAC", "LATAM"];
    
    private string _dbSuffix = null!;

    [Params(4, 8)]
    public int ParallelTasks { get; set; }

    [Params(10_000)]
    public int RecordCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Use unique suffix for each parameter combination to avoid conflicts
        _dbSuffix = $"{RecordCount}_{ParallelTasks}";
        
        _singleOptions = new DbContextOptionsBuilder<SingleTableDbContext>()
            .UseSqlite($"Data Source=concurrent_single_{_dbSuffix}.db")
            .Options;

        _indexedOptions = new DbContextOptionsBuilder<IndexedSingleTableDbContext>()
            .UseSqlite($"Data Source=concurrent_indexed_{_dbSuffix}.db")
            .Options;

        _shardedOptions = new DbContextOptionsBuilder<ShardedDbContext>()
            .UseSqlite($"Data Source=concurrent_sharded_{_dbSuffix}.db")
            .UseDtde()
            .Options;

        // Setup databases
        using (var ctx = new SingleTableDbContext(_singleOptions))
        {
            ctx.Database.EnsureDeleted();
            ctx.Database.EnsureCreated();
        }

        using (var ctx = new IndexedSingleTableDbContext(_indexedOptions))
        {
            ctx.Database.EnsureDeleted();
            ctx.Database.EnsureCreated();
        }

        using (var ctx = new ShardedDbContext(_shardedOptions))
        {
            ctx.Database.EnsureDeleted();
            ctx.Database.EnsureCreated();
        }

        // Generate and seed data
        _customers = DataGenerator.GenerateCustomers(RecordCount);
        _shardedCustomers = DataGenerator.GenerateShardedCustomers(RecordCount);

        SeedData();
    }

    private void SeedData()
    {
        const int batchSize = 5000;

        using var singleCtx = new SingleTableDbContext(_singleOptions);
        for (var i = 0; i < _customers.Count; i += batchSize)
        {
            singleCtx.Customers.AddRange(_customers.Skip(i).Take(batchSize));
            singleCtx.SaveChanges();
        }

        using var indexedCtx = new IndexedSingleTableDbContext(_indexedOptions);
        var idxCustomers = _customers.Select(c => new Customer
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

        for (var i = 0; i < idxCustomers.Count; i += batchSize)
        {
            indexedCtx.Customers.AddRange(idxCustomers.Skip(i).Take(batchSize));
            indexedCtx.SaveChanges();
        }

        using var shardedCtx = new ShardedDbContext(_shardedOptions);
        for (var i = 0; i < _shardedCustomers.Count; i += batchSize)
        {
            shardedCtx.Customers.AddRange(_shardedCustomers.Skip(i).Take(batchSize));
            shardedCtx.SaveChanges();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Clear SQLite connection pool to release file handles
        SqliteConnection.ClearAllPools();
        
        // Small delay to ensure connections are fully released
        Thread.Sleep(100);
        
        try { File.Delete($"concurrent_single_{_dbSuffix}.db"); } catch { /* ignore */ }
        try { File.Delete($"concurrent_indexed_{_dbSuffix}.db"); } catch { /* ignore */ }
        try { File.Delete($"concurrent_sharded_{_dbSuffix}.db"); } catch { /* ignore */ }
    }

    #region Parallel Reads - Same Region

    /// <summary>
    /// Single table: Multiple parallel reads querying same data range (high contention).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ParallelReadsSameRegion")]
    public int SingleTable_ParallelReads_SameRegion()
    {
        var results = new ConcurrentBag<int>();

        Parallel.For(0, ParallelTasks, _ =>
        {
            using var ctx = new SingleTableDbContext(_singleOptions);
            var count = ctx.Customers
                .Where(c => c.Region == "US")
                .Count();
            results.Add(count);
        });

        return results.Sum();
    }

    /// <summary>
    /// Indexed: Parallel reads with indexed region column.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ParallelReadsSameRegion")]
    public int Indexed_ParallelReads_SameRegion()
    {
        var results = new ConcurrentBag<int>();

        Parallel.For(0, ParallelTasks, _ =>
        {
            using var ctx = new IndexedSingleTableDbContext(_indexedOptions);
            var count = ctx.Customers
                .Where(c => c.Region == "US")
                .Count();
            results.Add(count);
        });

        return results.Sum();
    }

    /// <summary>
    /// Sharded: Parallel reads hitting same shard (tests shard isolation).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ParallelReadsSameRegion")]
    public int Sharded_ParallelReads_SameRegion()
    {
        var results = new ConcurrentBag<int>();

        Parallel.For(0, ParallelTasks, _ =>
        {
            using var ctx = new ShardedDbContext(_shardedOptions);
            var count = ctx.Customers
                .Where(c => c.Region == "US")
                .Count();
            results.Add(count);
        });

        return results.Sum();
    }

    #endregion

    #region Parallel Reads - Different Regions (Shard Affinity)

    /// <summary>
    /// Single table: Parallel reads querying different data ranges (low contention).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ParallelReadsDifferentRegions")]
    public int SingleTable_ParallelReads_DifferentRegions()
    {
        var results = new ConcurrentBag<int>();

        Parallel.For(0, ParallelTasks, i =>
        {
            using var ctx = new SingleTableDbContext(_singleOptions);
            var region = _regions[i % _regions.Length];
            var count = ctx.Customers
                .Where(c => c.Region == region)
                .Count();
            results.Add(count);
        });

        return results.Sum();
    }

    /// <summary>
    /// Indexed: Parallel reads with different regions (index spread).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ParallelReadsDifferentRegions")]
    public int Indexed_ParallelReads_DifferentRegions()
    {
        var results = new ConcurrentBag<int>();

        Parallel.For(0, ParallelTasks, i =>
        {
            using var ctx = new IndexedSingleTableDbContext(_indexedOptions);
            var region = _regions[i % _regions.Length];
            var count = ctx.Customers
                .Where(c => c.Region == region)
                .Count();
            results.Add(count);
        });

        return results.Sum();
    }

    /// <summary>
    /// Sharded: Parallel reads hitting different shards (optimal shard utilization).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ParallelReadsDifferentRegions")]
    public int Sharded_ParallelReads_DifferentRegions()
    {
        var results = new ConcurrentBag<int>();

        Parallel.For(0, ParallelTasks, i =>
        {
            using var ctx = new ShardedDbContext(_shardedOptions);
            var region = _regions[i % _regions.Length];
            var count = ctx.Customers
                .Where(c => c.Region == region)
                .Count();
            results.Add(count);
        });

        return results.Sum();
    }

    #endregion

    #region Parallel Aggregations

    /// <summary>
    /// Single table: Parallel aggregations on same table.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ParallelAggregation")]
    public List<(string Region, int Count)> SingleTable_ParallelAggregation()
    {
        var results = new ConcurrentBag<(string Region, int Count)>();

        Parallel.ForEach(_regions, region =>
        {
            using var ctx = new SingleTableDbContext(_singleOptions);
            var count = ctx.Customers.Count(c => c.Region == region);
            results.Add((region, count));
        });

        return results.ToList();
    }

    /// <summary>
    /// Sharded: Parallel aggregations per shard (natural parallelism).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ParallelAggregation")]
    public List<(string Region, int Count)> Sharded_ParallelAggregation()
    {
        var results = new ConcurrentBag<(string Region, int Count)>();

        Parallel.ForEach(_regions, region =>
        {
            using var ctx = new ShardedDbContext(_shardedOptions);
            var count = ctx.Customers.Count(c => c.Region == region);
            results.Add((region, count));
        });

        return results.ToList();
    }

    #endregion

    #region Parallel Complex Queries

    /// <summary>
    /// Single table: Parallel complex queries with ordering.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ParallelComplexQuery")]
    public int SingleTable_ParallelComplexQueries()
    {
        var results = new ConcurrentBag<int>();

        Parallel.For(0, ParallelTasks, i =>
        {
            using var ctx = new SingleTableDbContext(_singleOptions);
            var region = _regions[i % _regions.Length];
            var customers = ctx.Customers
                .Where(c => c.Region == region && c.Tier >= CustomerTier.Silver)
                .OrderByDescending(c => c.CreatedAt)
                .Take(50)
                .ToList();
            results.Add(customers.Count);
        });

        return results.Sum();
    }

    /// <summary>
    /// Indexed: Parallel complex queries with index support.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ParallelComplexQuery")]
    public int Indexed_ParallelComplexQueries()
    {
        var results = new ConcurrentBag<int>();

        Parallel.For(0, ParallelTasks, i =>
        {
            using var ctx = new IndexedSingleTableDbContext(_indexedOptions);
            var region = _regions[i % _regions.Length];
            var customers = ctx.Customers
                .Where(c => c.Region == region && c.Tier >= CustomerTier.Silver)
                .OrderByDescending(c => c.CreatedAt)
                .Take(50)
                .ToList();
            results.Add(customers.Count);
        });

        return results.Sum();
    }

    /// <summary>
    /// Sharded: Parallel complex queries on sharded tables.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ParallelComplexQuery")]
    public int Sharded_ParallelComplexQueries()
    {
        var results = new ConcurrentBag<int>();

        Parallel.For(0, ParallelTasks, i =>
        {
            using var ctx = new ShardedDbContext(_shardedOptions);
            var region = _regions[i % _regions.Length];
            var customers = ctx.Customers
                .Where(c => c.Region == region && c.Tier >= CustomerTier.Silver)
                .OrderByDescending(c => c.CreatedAt)
                .Take(50)
                .ToList();
            results.Add(customers.Count);
        });

        return results.Sum();
    }

    #endregion

    #region Mixed Read/Write Workload

    /// <summary>
    /// Single table: Mixed read/write workload (realistic scenario).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("MixedWorkload")]
    public int SingleTable_MixedWorkload()
    {
        var operations = new ConcurrentBag<int>();
        var random = new Random(42);

        Parallel.For(0, ParallelTasks, i =>
        {
            using var ctx = new SingleTableDbContext(_singleOptions);
            var region = _regions[i % _regions.Length];

            // 80% reads, 20% writes
            if (random.Next(100) < 80)
            {
                var count = ctx.Customers
                    .Where(c => c.Region == region)
                    .Take(20)
                    .Count();
                operations.Add(count);
            }
            else
            {
                var customer = new Customer
                {
                    Name = $"Mixed_{i}",
                    Email = $"mixed_{i}_{Guid.NewGuid()}@test.com",
                    Region = region,
                    Tier = CustomerTier.Standard,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };
                ctx.Customers.Add(customer);
                operations.Add(ctx.SaveChanges());
            }
        });

        return operations.Sum();
    }

    /// <summary>
    /// Sharded: Mixed read/write with shard-local operations.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("MixedWorkload")]
    public int Sharded_MixedWorkload()
    {
        var operations = new ConcurrentBag<int>();
        var random = new Random(42);

        Parallel.For(0, ParallelTasks, i =>
        {
            using var ctx = new ShardedDbContext(_shardedOptions);
            var region = _regions[i % _regions.Length];

            // 80% reads, 20% writes
            if (random.Next(100) < 80)
            {
                var count = ctx.Customers
                    .Where(c => c.Region == region)
                    .Take(20)
                    .Count();
                operations.Add(count);
            }
            else
            {
                var customer = new ShardedCustomer
                {
                    Name = $"Mixed_{i}",
                    Email = $"mixed_{i}_{Guid.NewGuid()}@test.com",
                    Region = region,
                    Tier = CustomerTier.Standard,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };
                ctx.Customers.Add(customer);
                operations.Add(ctx.SaveChanges());
            }
        });

        return operations.Sum();
    }

    #endregion

    #region Hot Spot Simulation

    /// <summary>
    /// Single table: All parallel tasks query same record (worst case contention).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("HotSpot")]
    public int SingleTable_HotSpot()
    {
        var results = new ConcurrentBag<int>();

        Parallel.For(0, ParallelTasks, _ =>
        {
            using var ctx = new SingleTableDbContext(_singleOptions);
            var customer = ctx.Customers
                .OrderBy(c => c.Id)
                .FirstOrDefault();
            results.Add(customer != null ? 1 : 0);
        });

        return results.Sum();
    }

    /// <summary>
    /// Sharded: Hot spot query with shard routing.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("HotSpot")]
    public int Sharded_HotSpot()
    {
        var results = new ConcurrentBag<int>();

        Parallel.For(0, ParallelTasks, _ =>
        {
            using var ctx = new ShardedDbContext(_shardedOptions);
            var customer = ctx.Customers
                .Where(c => c.Region == "US")
                .OrderBy(c => c.Id)
                .FirstOrDefault();
            results.Add(customer != null ? 1 : 0);
        });

        return results.Sum();
    }

    #endregion

    #region Scatter-Gather Pattern

    /// <summary>
    /// Single table: Query that touches all data (full table scan).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ScatterGather")]
    public List<(string Region, decimal Total)> SingleTable_ScatterGather()
    {
        using var ctx = new SingleTableDbContext(_singleOptions);
        return ctx.Customers
            .GroupBy(c => c.Region)
            .Select(g => new { Region = g.Key, Count = g.Count() })
            .ToList()
            .Select(x => (x.Region, (decimal)x.Count))
            .ToList();
    }

    /// <summary>
    /// Sharded: Parallel scatter-gather across all shards with merge.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ScatterGather")]
    public List<(string Region, decimal Total)> Sharded_ScatterGather_Parallel()
    {
        var results = new ConcurrentDictionary<string, int>();

        // Scatter: Query each shard in parallel
        Parallel.ForEach(_regions, region =>
        {
            using var ctx = new ShardedDbContext(_shardedOptions);
            var count = ctx.Customers.Count(c => c.Region == region);
            results.AddOrUpdate(region, count, (_, existing) => existing + count);
        });

        // Gather: Merge results
        return results.Select(x => (x.Key, (decimal)x.Value)).ToList();
    }

    #endregion

    #region Read-After-Write Consistency

    /// <summary>
    /// Single table: Write then immediate read (consistency check).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ReadAfterWrite")]
    public int SingleTable_ReadAfterWrite()
    {
        var results = new ConcurrentBag<int>();

        Parallel.For(0, ParallelTasks, i =>
        {
            using var ctx = new SingleTableDbContext(_singleOptions);

            // Write
            var email = $"raw_{i}_{Guid.NewGuid()}@test.com";
            ctx.Customers.Add(new Customer
            {
                Name = $"RAW_Test_{i}",
                Email = email,
                Region = _regions[i % _regions.Length],
                Tier = CustomerTier.Standard,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            ctx.SaveChanges();

            // Immediate read
            var found = ctx.Customers.Any(c => c.Email == email);
            results.Add(found ? 1 : 0);
        });

        return results.Sum();
    }

    /// <summary>
    /// Sharded: Write then read in sharded context (shard-local consistency).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ReadAfterWrite")]
    public int Sharded_ReadAfterWrite()
    {
        var results = new ConcurrentBag<int>();

        Parallel.For(0, ParallelTasks, i =>
        {
            using var ctx = new ShardedDbContext(_shardedOptions);
            var region = _regions[i % _regions.Length];

            // Write
            var email = $"raw_{i}_{Guid.NewGuid()}@test.com";
            ctx.Customers.Add(new ShardedCustomer
            {
                Name = $"RAW_Test_{i}",
                Email = email,
                Region = region,
                Tier = CustomerTier.Standard,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            ctx.SaveChanges();

            // Immediate read with shard key
            var found = ctx.Customers.Any(c => c.Email == email && c.Region == region);
            results.Add(found ? 1 : 0);
        });

        return results.Sum();
    }

    #endregion
}
