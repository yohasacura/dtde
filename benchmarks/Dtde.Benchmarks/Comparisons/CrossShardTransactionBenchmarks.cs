using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dtde.Benchmarks.Data;
using Dtde.Benchmarks.Entities;
using Dtde.EntityFramework.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Benchmarks.Comparisons;

/// <summary>
/// Benchmarks for cross-shard transaction operations.
/// Compares single-shard transactions vs cross-shard transactions with 2PC protocol.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class CrossShardTransactionBenchmarks
{
    private DbContextOptions<ShardedDbContext> _shardedOptions = null!;
    private DbContextOptions<SingleTableDbContext> _singleOptions = null!;

    private List<ShardedCustomer> _shardedCustomers = null!;
    private List<Customer> _customers = null!;
    private readonly string[] _regions = ["US", "EU", "APAC", "LATAM"];

    private string _dbSuffix = null!;

    public static IEnumerable<int> RecordCountSource => BenchmarkConfig.CrossShardRecordCounts;

    [ParamsSource(nameof(RecordCountSource))]
    public int RecordCount { get; set; }

    public static IEnumerable<int> BatchSizeSource => BenchmarkConfig.CrossShardBatchSizes;

    [ParamsSource(nameof(BatchSizeSource))]
    public int BatchSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _dbSuffix = $"{RecordCount}_{BatchSize}";

        _singleOptions = new DbContextOptionsBuilder<SingleTableDbContext>()
            .UseSqlite($"Data Source=cstx_single_{_dbSuffix}.db")
            .Options;

        _shardedOptions = new DbContextOptionsBuilder<ShardedDbContext>()
            .UseSqlite($"Data Source=cstx_sharded_{_dbSuffix}.db")
            .UseDtde()
            .Options;

        // Setup databases
        using (var ctx = new SingleTableDbContext(_singleOptions))
        {
            ctx.Database.EnsureDeleted();
            ctx.Database.EnsureCreated();
        }

        using (var ctx = new ShardedDbContext(_shardedOptions))
        {
            ctx.Database.EnsureDeleted();
            ctx.Database.EnsureCreated();
        }

        // Generate data
        _customers = DataGenerator.GenerateCustomers(RecordCount);
        _shardedCustomers = DataGenerator.GenerateShardedCustomers(RecordCount);

        SeedData();
    }

    private void SeedData()
    {
        const int batchSize = 1000;

        using var singleCtx = new SingleTableDbContext(_singleOptions);
        for (var i = 0; i < _customers.Count; i += batchSize)
        {
            singleCtx.Customers.AddRange(_customers.Skip(i).Take(batchSize));
            singleCtx.SaveChanges();
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
        SqliteConnection.ClearAllPools();
        Thread.Sleep(100);

        try { File.Delete($"cstx_single_{_dbSuffix}.db"); } catch { /* ignore */ }
        try { File.Delete($"cstx_sharded_{_dbSuffix}.db"); } catch { /* ignore */ }
    }

    #region Single Shard vs Cross-Shard Insert

    /// <summary>
    /// Single table: Batch insert within one database (no transaction coordination).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("BatchInsert")]
    public async Task<int> SingleTable_BatchInsert()
    {
        var customers = Enumerable.Range(0, BatchSize)
            .Select(i => new Customer
            {
                Name = $"Customer_{Guid.NewGuid():N}",
                Email = $"{Guid.NewGuid():N}@benchmark.test",
                Region = _regions[i % 4],
                City = "Test City",
                Country = "Test Country",
                Tier = CustomerTier.Standard,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            }).ToList();

        await using var ctx = new SingleTableDbContext(_singleOptions);
        await using var transaction = await ctx.Database.BeginTransactionAsync();

        ctx.Customers.AddRange(customers);
        var result = await ctx.SaveChangesAsync();

        await transaction.CommitAsync();
        return result;
    }

    /// <summary>
    /// Sharded: Single-shard insert (all entities go to the same shard).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("BatchInsert")]
    public async Task<int> Sharded_SingleShardInsert()
    {
        var sameRegion = _regions[0];
        var customers = Enumerable.Range(0, BatchSize)
            .Select(_ => new ShardedCustomer
            {
                Name = $"Customer_{Guid.NewGuid():N}",
                Email = $"{Guid.NewGuid():N}@benchmark.test",
                Region = sameRegion, // All to same shard
                City = "Test City",
                Country = "Test Country",
                Tier = CustomerTier.Standard,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            }).ToList();

        await using var ctx = new ShardedDbContext(_shardedOptions);
        await using var transaction = await ctx.Database.BeginTransactionAsync();

        ctx.Customers.AddRange(customers);
        var result = await ctx.SaveChangesAsync();

        await transaction.CommitAsync();
        return result;
    }

    /// <summary>
    /// Sharded: Cross-shard insert (entities distributed across multiple shards).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("BatchInsert")]
    public async Task<int> Sharded_CrossShardInsert()
    {
        var customers = Enumerable.Range(0, BatchSize)
            .Select(i => new ShardedCustomer
            {
                Name = $"Customer_{Guid.NewGuid():N}",
                Email = $"{Guid.NewGuid():N}@benchmark.test",
                Region = _regions[i % 4], // Distributed across shards
                City = "Test City",
                Country = "Test Country",
                Tier = CustomerTier.Standard,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            }).ToList();

        await using var ctx = new ShardedDbContext(_shardedOptions);

        // This requires cross-shard coordination
        ctx.Customers.AddRange(customers);
        var result = await ctx.SaveChangesAsync();

        return result;
    }

    #endregion

    #region Single Shard vs Cross-Shard Update

    /// <summary>
    /// Single table: Batch update within one transaction.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("BatchUpdate")]
    public async Task<int> SingleTable_BatchUpdate()
    {
        await using var ctx = new SingleTableDbContext(_singleOptions);
        await using var transaction = await ctx.Database.BeginTransactionAsync();

        var toUpdate = await ctx.Customers
            .Take(BatchSize)
            .ToListAsync();

        foreach (var customer in toUpdate)
        {
            customer.City = $"Updated_{DateTime.UtcNow.Ticks}";
        }

        var result = await ctx.SaveChangesAsync();
        await transaction.CommitAsync();
        return result;
    }

    /// <summary>
    /// Sharded: Single-shard update (all from same region).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("BatchUpdate")]
    public async Task<int> Sharded_SingleShardUpdate()
    {
        var sameRegion = _regions[0];

        await using var ctx = new ShardedDbContext(_shardedOptions);
        await using var transaction = await ctx.Database.BeginTransactionAsync();

        var toUpdate = await ctx.Customers
            .Where(c => c.Region == sameRegion)
            .Take(BatchSize)
            .ToListAsync();

        foreach (var customer in toUpdate)
        {
            customer.City = $"Updated_{DateTime.UtcNow.Ticks}";
        }

        var result = await ctx.SaveChangesAsync();
        await transaction.CommitAsync();
        return result;
    }

    /// <summary>
    /// Sharded: Cross-shard update (from multiple regions).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("BatchUpdate")]
    public async Task<int> Sharded_CrossShardUpdate()
    {
        await using var ctx = new ShardedDbContext(_shardedOptions);

        // Get customers from multiple regions
        var toUpdate = new List<ShardedCustomer>();
        var perRegion = BatchSize / _regions.Length;

        foreach (var region in _regions)
        {
            var regionCustomers = await ctx.Customers
                .Where(c => c.Region == region)
                .Take(perRegion)
                .ToListAsync();
            toUpdate.AddRange(regionCustomers);
        }

        foreach (var customer in toUpdate)
        {
            customer.City = $"Updated_{DateTime.UtcNow.Ticks}";
        }

        var result = await ctx.SaveChangesAsync();
        return result;
    }

    #endregion

    #region Single vs Sharded Write Overhead

    /// <summary>
    /// Single table write without transaction.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("WriteOverhead")]
    public async Task<int> SingleTable_WriteNoTransaction()
    {
        var customers = Enumerable.Range(0, BatchSize)
            .Select(i => new Customer
            {
                Name = $"Customer_{Guid.NewGuid():N}",
                Email = $"{Guid.NewGuid():N}@benchmark.test",
                Region = _regions[i % 4],
                City = "Test City",
                Country = "Test Country",
                Tier = CustomerTier.Standard,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            }).ToList();

        await using var ctx = new SingleTableDbContext(_singleOptions);
        ctx.Customers.AddRange(customers);
        return await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Sharded write to single shard without explicit transaction.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("WriteOverhead")]
    public async Task<int> Sharded_SingleShardNoTransaction()
    {
        var sameRegion = _regions[0];
        var customers = Enumerable.Range(0, BatchSize)
            .Select(_ => new ShardedCustomer
            {
                Name = $"Customer_{Guid.NewGuid():N}",
                Email = $"{Guid.NewGuid():N}@benchmark.test",
                Region = sameRegion,
                City = "Test City",
                Country = "Test Country",
                Tier = CustomerTier.Standard,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            }).ToList();

        await using var ctx = new ShardedDbContext(_shardedOptions);
        ctx.Customers.AddRange(customers);
        return await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Sharded write to multiple shards without explicit transaction.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("WriteOverhead")]
    public async Task<int> Sharded_MultiShardNoTransaction()
    {
        var customers = Enumerable.Range(0, BatchSize)
            .Select(i => new ShardedCustomer
            {
                Name = $"Customer_{Guid.NewGuid():N}",
                Email = $"{Guid.NewGuid():N}@benchmark.test",
                Region = _regions[i % 4],
                City = "Test City",
                Country = "Test Country",
                Tier = CustomerTier.Standard,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            }).ToList();

        await using var ctx = new ShardedDbContext(_shardedOptions);
        ctx.Customers.AddRange(customers);
        return await ctx.SaveChangesAsync();
    }

    #endregion

    #region Read-Heavy vs Write-Heavy Mixed Workloads

    /// <summary>
    /// Read-heavy workload on single table (90% reads, 10% writes).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("MixedWorkload")]
    public async Task<int> SingleTable_ReadHeavyMixed()
    {
        var operations = 0;

        await using var ctx = new SingleTableDbContext(_singleOptions);

        // 90% reads
        for (int i = 0; i < 9; i++)
        {
            var count = await ctx.Customers
                .Where(c => c.Region == _regions[i % 4])
                .CountAsync();
            operations += count > 0 ? 1 : 0;
        }

        // 10% writes
        var customer = new Customer
        {
            Name = $"Customer_{Guid.NewGuid():N}",
            Email = $"{Guid.NewGuid():N}@benchmark.test",
            Region = _regions[0],
            City = "Test City",
            Country = "Test Country",
            Tier = CustomerTier.Standard,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        ctx.Customers.Add(customer);
        operations += await ctx.SaveChangesAsync();

        return operations;
    }

    /// <summary>
    /// Read-heavy workload on sharded table (90% reads, 10% writes).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("MixedWorkload")]
    public async Task<int> Sharded_ReadHeavyMixed()
    {
        var operations = 0;

        await using var ctx = new ShardedDbContext(_shardedOptions);

        // 90% reads - each targets specific shard
        for (int i = 0; i < 9; i++)
        {
            var count = await ctx.Customers
                .Where(c => c.Region == _regions[i % 4])
                .CountAsync();
            operations += count > 0 ? 1 : 0;
        }

        // 10% writes
        var customer = new ShardedCustomer
        {
            Name = $"Customer_{Guid.NewGuid():N}",
            Email = $"{Guid.NewGuid():N}@benchmark.test",
            Region = _regions[0],
            City = "Test City",
            Country = "Test Country",
            Tier = CustomerTier.Standard,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        ctx.Customers.Add(customer);
        operations += await ctx.SaveChangesAsync();

        return operations;
    }

    /// <summary>
    /// Write-heavy workload on single table (30% reads, 70% writes).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("MixedWorkload")]
    public async Task<int> SingleTable_WriteHeavyMixed()
    {
        var operations = 0;

        await using var ctx = new SingleTableDbContext(_singleOptions);
        await using var transaction = await ctx.Database.BeginTransactionAsync();

        // 30% reads
        for (int i = 0; i < 3; i++)
        {
            var count = await ctx.Customers
                .Where(c => c.Region == _regions[i % 4])
                .CountAsync();
            operations += count > 0 ? 1 : 0;
        }

        // 70% writes
        for (int i = 0; i < 7; i++)
        {
            var customer = new Customer
            {
                Name = $"Customer_{Guid.NewGuid():N}",
                Email = $"{Guid.NewGuid():N}@benchmark.test",
                Region = _regions[i % 4],
                City = "Test City",
                Country = "Test Country",
                Tier = CustomerTier.Standard,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };
            ctx.Customers.Add(customer);
        }

        operations += await ctx.SaveChangesAsync();
        await transaction.CommitAsync();

        return operations;
    }

    /// <summary>
    /// Write-heavy workload on sharded table (30% reads, 70% writes).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("MixedWorkload")]
    public async Task<int> Sharded_WriteHeavyMixed()
    {
        var operations = 0;

        await using var ctx = new ShardedDbContext(_shardedOptions);

        // 30% reads
        for (int i = 0; i < 3; i++)
        {
            var count = await ctx.Customers
                .Where(c => c.Region == _regions[i % 4])
                .CountAsync();
            operations += count > 0 ? 1 : 0;
        }

        // 70% writes - distributed across shards
        for (int i = 0; i < 7; i++)
        {
            var customer = new ShardedCustomer
            {
                Name = $"Customer_{Guid.NewGuid():N}",
                Email = $"{Guid.NewGuid():N}@benchmark.test",
                Region = _regions[i % 4],
                City = "Test City",
                Country = "Test Country",
                Tier = CustomerTier.Standard,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };
            ctx.Customers.Add(customer);
        }

        operations += await ctx.SaveChangesAsync();

        return operations;
    }

    #endregion

    #region Shard-Aware Reads

    /// <summary>
    /// Single table: Query by non-shard key.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ShardAwareReads")]
    public async Task<int> SingleTable_QueryByName()
    {
        await using var ctx = new SingleTableDbContext(_singleOptions);
        return await ctx.Customers
            .Where(c => c.Name.StartsWith("Cust"))
            .Take(100)
            .CountAsync();
    }

    /// <summary>
    /// Sharded: Query by shard key (region) - targets single shard.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ShardAwareReads")]
    public async Task<int> Sharded_QueryByShardKey()
    {
        await using var ctx = new ShardedDbContext(_shardedOptions);
        return await ctx.Customers
            .Where(c => c.Region == "US")
            .Take(100)
            .CountAsync();
    }

    /// <summary>
    /// Sharded: Query by non-shard key - requires scatter-gather.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ShardAwareReads")]
    public async Task<int> Sharded_QueryByNonShardKey()
    {
        await using var ctx = new ShardedDbContext(_shardedOptions);
        return await ctx.Customers
            .Where(c => c.Name.StartsWith("Cust"))
            .Take(100)
            .CountAsync();
    }

    /// <summary>
    /// Sharded: Query all shards with aggregation.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ShardAwareReads")]
    public async Task<int> Sharded_QueryAllShardsCount()
    {
        await using var ctx = new ShardedDbContext(_shardedOptions);
        return await ctx.Customers.CountAsync();
    }

    #endregion

    #region Batch Size Impact

    /// <summary>
    /// Single table: Insert with explicit batch boundaries.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("BatchSizeImpact")]
    public async Task<int> SingleTable_BatchedInsert()
    {
        await using var ctx = new SingleTableDbContext(_singleOptions);
        var total = 0;
        var batchCount = 5;
        var itemsPerBatch = BatchSize / batchCount;

        for (int batch = 0; batch < batchCount; batch++)
        {
            var customers = Enumerable.Range(0, itemsPerBatch)
                .Select(i => new Customer
                {
                    Name = $"Customer_{Guid.NewGuid():N}",
                    Email = $"{Guid.NewGuid():N}@benchmark.test",
                    Region = _regions[(batch * itemsPerBatch + i) % 4],
                    City = "Test City",
                    Country = "Test Country",
                    Tier = CustomerTier.Standard,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                }).ToList();

            ctx.Customers.AddRange(customers);
            total += await ctx.SaveChangesAsync();
        }

        return total;
    }

    /// <summary>
    /// Sharded: Insert with explicit batch boundaries (same shard per batch).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("BatchSizeImpact")]
    public async Task<int> Sharded_BatchedInsertSameShard()
    {
        await using var ctx = new ShardedDbContext(_shardedOptions);
        var total = 0;
        var batchCount = 5;
        var itemsPerBatch = BatchSize / batchCount;

        for (int batch = 0; batch < batchCount; batch++)
        {
            var region = _regions[batch % _regions.Length];
            var customers = Enumerable.Range(0, itemsPerBatch)
                .Select(_ => new ShardedCustomer
                {
                    Name = $"Customer_{Guid.NewGuid():N}",
                    Email = $"{Guid.NewGuid():N}@benchmark.test",
                    Region = region, // Same region per batch
                    City = "Test City",
                    Country = "Test Country",
                    Tier = CustomerTier.Standard,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                }).ToList();

            ctx.Customers.AddRange(customers);
            total += await ctx.SaveChangesAsync();
        }

        return total;
    }

    /// <summary>
    /// Sharded: Insert with explicit batch boundaries (mixed shards per batch).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("BatchSizeImpact")]
    public async Task<int> Sharded_BatchedInsertMixedShards()
    {
        await using var ctx = new ShardedDbContext(_shardedOptions);
        var total = 0;
        var batchCount = 5;
        var itemsPerBatch = BatchSize / batchCount;

        for (int batch = 0; batch < batchCount; batch++)
        {
            var customers = Enumerable.Range(0, itemsPerBatch)
                .Select(i => new ShardedCustomer
                {
                    Name = $"Customer_{Guid.NewGuid():N}",
                    Email = $"{Guid.NewGuid():N}@benchmark.test",
                    Region = _regions[i % 4], // Different regions per item
                    City = "Test City",
                    Country = "Test Country",
                    Tier = CustomerTier.Standard,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                }).ToList();

            ctx.Customers.AddRange(customers);
            total += await ctx.SaveChangesAsync();
        }

        return total;
    }

    #endregion
}
