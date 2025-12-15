using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dtde.Benchmarks.Data;
using Dtde.Benchmarks.Entities;
using Dtde.EntityFramework.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Benchmarks.Comparisons;

/// <summary>
/// Benchmarks for date-based sharding scenarios.
/// Tests how date-based partitioning affects query performance for time-series data.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class DateShardingBenchmarks
{
    private DbContextOptions<SingleTableDbContext> _singleOptions = null!;
    private DbContextOptions<IndexedSingleTableDbContext> _indexedOptions = null!;
    private DbContextOptions<ShardedDbContext> _shardedOptions = null!;

    private List<Transaction> _transactions = null!;
    private List<ShardedTransaction> _shardedTransactions = null!;

    private DateTime _startDate;
    private DateTime _midDate;
    private DateTime _endDate;

    private string _dbSuffix = null!;

    public static IEnumerable<int> RecordCountSource => BenchmarkConfig.DateShardingRecordCounts;

    [ParamsSource(nameof(RecordCountSource))]
    public int RecordCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _dbSuffix = $"{RecordCount}";

        _singleOptions = new DbContextOptionsBuilder<SingleTableDbContext>()
            .UseSqlite($"Data Source=datesha_single_{_dbSuffix}.db")
            .Options;

        _indexedOptions = new DbContextOptionsBuilder<IndexedSingleTableDbContext>()
            .UseSqlite($"Data Source=datesha_indexed_{_dbSuffix}.db")
            .Options;

        _shardedOptions = new DbContextOptionsBuilder<ShardedDbContext>()
            .UseSqlite($"Data Source=datesha_sharded_{_dbSuffix}.db")
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

        // Generate transactions spanning 12 months
        _startDate = new DateTime(2024, 1, 1);
        _endDate = new DateTime(2024, 12, 31);
        _midDate = new DateTime(2024, 6, 15);

        _transactions = DataGenerator.GenerateTransactions(RecordCount, _startDate, _endDate);
        _shardedTransactions = DataGenerator.GenerateShardedTransactions(RecordCount, _startDate, _endDate);

        SeedData();
    }

    private void SeedData()
    {
        const int batchSize = 5000;

        using var singleCtx = new SingleTableDbContext(_singleOptions);
        for (var i = 0; i < _transactions.Count; i += batchSize)
        {
            singleCtx.Transactions.AddRange(_transactions.Skip(i).Take(batchSize));
            singleCtx.SaveChanges();
        }

        using var indexedCtx = new IndexedSingleTableDbContext(_indexedOptions);
        var idxTransactions = _transactions.Select(t => new Transaction
        {
            TransactionRef = t.TransactionRef + "_idx",
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

        for (var i = 0; i < idxTransactions.Count; i += batchSize)
        {
            indexedCtx.Transactions.AddRange(idxTransactions.Skip(i).Take(batchSize));
            indexedCtx.SaveChanges();
        }

        using var shardedCtx = new ShardedDbContext(_shardedOptions);
        for (var i = 0; i < _shardedTransactions.Count; i += batchSize)
        {
            shardedCtx.Transactions.AddRange(_shardedTransactions.Skip(i).Take(batchSize));
            shardedCtx.SaveChanges();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Thread.Sleep(100);

        try { File.Delete($"datesha_single_{_dbSuffix}.db"); } catch { /* ignore */ }
        try { File.Delete($"datesha_indexed_{_dbSuffix}.db"); } catch { /* ignore */ }
        try { File.Delete($"datesha_sharded_{_dbSuffix}.db"); } catch { /* ignore */ }
    }

    #region Single Month Queries

    /// <summary>
    /// Single table: Query transactions for one specific month.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("SingleMonth")]
    public int SingleTable_QuerySingleMonth()
    {
        var monthStart = new DateTime(2024, 6, 1);
        var monthEnd = new DateTime(2024, 6, 30);

        using var ctx = new SingleTableDbContext(_singleOptions);
        return ctx.Transactions
            .Where(t => t.TransactionDate >= monthStart && t.TransactionDate <= monthEnd)
            .Count();
    }

    /// <summary>
    /// Indexed table: Query transactions for one specific month.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("SingleMonth")]
    public int Indexed_QuerySingleMonth()
    {
        var monthStart = new DateTime(2024, 6, 1);
        var monthEnd = new DateTime(2024, 6, 30);

        using var ctx = new IndexedSingleTableDbContext(_indexedOptions);
        return ctx.Transactions
            .Where(t => t.TransactionDate >= monthStart && t.TransactionDate <= monthEnd)
            .Count();
    }

    /// <summary>
    /// Sharded table (date-based): Query transactions for one specific month.
    /// Date-based sharding should prune to a single shard.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("SingleMonth")]
    public int Sharded_QuerySingleMonth()
    {
        var monthStart = new DateTime(2024, 6, 1);
        var monthEnd = new DateTime(2024, 6, 30);

        using var ctx = new ShardedDbContext(_shardedOptions);
        return ctx.Transactions
            .Where(t => t.TransactionDate >= monthStart && t.TransactionDate <= monthEnd)
            .Count();
    }

    #endregion

    #region Quarter Queries (3 months)

    /// <summary>
    /// Single table: Query transactions for a quarter.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("QuarterQuery")]
    public int SingleTable_QueryQuarter()
    {
        var q2Start = new DateTime(2024, 4, 1);
        var q2End = new DateTime(2024, 6, 30);

        using var ctx = new SingleTableDbContext(_singleOptions);
        return ctx.Transactions
            .Where(t => t.TransactionDate >= q2Start && t.TransactionDate <= q2End)
            .Count();
    }

    /// <summary>
    /// Indexed table: Query transactions for a quarter.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("QuarterQuery")]
    public int Indexed_QueryQuarter()
    {
        var q2Start = new DateTime(2024, 4, 1);
        var q2End = new DateTime(2024, 6, 30);

        using var ctx = new IndexedSingleTableDbContext(_indexedOptions);
        return ctx.Transactions
            .Where(t => t.TransactionDate >= q2Start && t.TransactionDate <= q2End)
            .Count();
    }

    /// <summary>
    /// Sharded table: Query transactions for a quarter (3 monthly shards).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("QuarterQuery")]
    public int Sharded_QueryQuarter()
    {
        var q2Start = new DateTime(2024, 4, 1);
        var q2End = new DateTime(2024, 6, 30);

        using var ctx = new ShardedDbContext(_shardedOptions);
        return ctx.Transactions
            .Where(t => t.TransactionDate >= q2Start && t.TransactionDate <= q2End)
            .Count();
    }

    #endregion

    #region Full Year Queries (All Shards)

    /// <summary>
    /// Single table: Query all transactions for the year.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("FullYear")]
    public int SingleTable_QueryFullYear()
    {
        using var ctx = new SingleTableDbContext(_singleOptions);
        return ctx.Transactions
            .Where(t => t.TransactionDate >= _startDate && t.TransactionDate <= _endDate)
            .Count();
    }

    /// <summary>
    /// Indexed table: Query all transactions for the year.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("FullYear")]
    public int Indexed_QueryFullYear()
    {
        using var ctx = new IndexedSingleTableDbContext(_indexedOptions);
        return ctx.Transactions
            .Where(t => t.TransactionDate >= _startDate && t.TransactionDate <= _endDate)
            .Count();
    }

    /// <summary>
    /// Sharded table: Query all transactions for the year (all 12 shards).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("FullYear")]
    public int Sharded_QueryFullYear()
    {
        using var ctx = new ShardedDbContext(_shardedOptions);
        return ctx.Transactions
            .Where(t => t.TransactionDate >= _startDate && t.TransactionDate <= _endDate)
            .Count();
    }

    #endregion

    #region Aggregation by Date Range

    /// <summary>
    /// Single table: Sum transactions by month.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("MonthlyAggregation")]
    public decimal SingleTable_SumByMonth()
    {
        var monthStart = new DateTime(2024, 6, 1);
        var monthEnd = new DateTime(2024, 6, 30);

        using var ctx = new SingleTableDbContext(_singleOptions);
        return ctx.Transactions
            .Where(t => t.TransactionDate >= monthStart && t.TransactionDate <= monthEnd)
            .Sum(t => t.Amount);
    }

    /// <summary>
    /// Indexed table: Sum transactions by month.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("MonthlyAggregation")]
    public decimal Indexed_SumByMonth()
    {
        var monthStart = new DateTime(2024, 6, 1);
        var monthEnd = new DateTime(2024, 6, 30);

        using var ctx = new IndexedSingleTableDbContext(_indexedOptions);
        return ctx.Transactions
            .Where(t => t.TransactionDate >= monthStart && t.TransactionDate <= monthEnd)
            .Sum(t => t.Amount);
    }

    /// <summary>
    /// Sharded table: Sum transactions by month (single shard).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("MonthlyAggregation")]
    public decimal Sharded_SumByMonth()
    {
        var monthStart = new DateTime(2024, 6, 1);
        var monthEnd = new DateTime(2024, 6, 30);

        using var ctx = new ShardedDbContext(_shardedOptions);
        return ctx.Transactions
            .Where(t => t.TransactionDate >= monthStart && t.TransactionDate <= monthEnd)
            .Sum(t => t.Amount);
    }

    #endregion

    #region Account-Based Queries Across Date Shards

    /// <summary>
    /// Single table: Get transactions for a specific account across all time.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("AccountQuery")]
    public int SingleTable_QueryByAccount()
    {
        var accountNumber = _transactions[0].AccountNumber;

        using var ctx = new SingleTableDbContext(_singleOptions);
        return ctx.Transactions
            .Where(t => t.AccountNumber == accountNumber)
            .Count();
    }

    /// <summary>
    /// Indexed table: Get transactions for a specific account across all time.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("AccountQuery")]
    public int Indexed_QueryByAccount()
    {
        var accountNumber = _transactions[0].AccountNumber;

        using var ctx = new IndexedSingleTableDbContext(_indexedOptions);
        return ctx.Transactions
            .Where(t => t.AccountNumber == accountNumber)
            .Count();
    }

    /// <summary>
    /// Sharded table: Get transactions for a specific account (must query all date shards).
    /// This demonstrates the scatter-gather pattern.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("AccountQuery")]
    public int Sharded_QueryByAccount()
    {
        var accountNumber = _shardedTransactions[0].AccountNumber;

        using var ctx = new ShardedDbContext(_shardedOptions);
        return ctx.Transactions
            .Where(t => t.AccountNumber == accountNumber)
            .Count();
    }

    #endregion

    #region Account + Date Range Queries (Optimized for Date Sharding)

    /// <summary>
    /// Single table: Get transactions for account in a date range.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("AccountDateRange")]
    public int SingleTable_QueryByAccountAndDateRange()
    {
        var accountNumber = _transactions[0].AccountNumber;
        var monthStart = new DateTime(2024, 6, 1);
        var monthEnd = new DateTime(2024, 6, 30);

        using var ctx = new SingleTableDbContext(_singleOptions);
        return ctx.Transactions
            .Where(t => t.AccountNumber == accountNumber &&
                        t.TransactionDate >= monthStart &&
                        t.TransactionDate <= monthEnd)
            .Count();
    }

    /// <summary>
    /// Indexed table: Get transactions for account in a date range.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("AccountDateRange")]
    public int Indexed_QueryByAccountAndDateRange()
    {
        var accountNumber = _transactions[0].AccountNumber;
        var monthStart = new DateTime(2024, 6, 1);
        var monthEnd = new DateTime(2024, 6, 30);

        using var ctx = new IndexedSingleTableDbContext(_indexedOptions);
        return ctx.Transactions
            .Where(t => t.AccountNumber == accountNumber &&
                        t.TransactionDate >= monthStart &&
                        t.TransactionDate <= monthEnd)
            .Count();
    }

    /// <summary>
    /// Sharded table: Get transactions for account in a date range (date filter prunes shards).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("AccountDateRange")]
    public int Sharded_QueryByAccountAndDateRange()
    {
        var accountNumber = _shardedTransactions[0].AccountNumber;
        var monthStart = new DateTime(2024, 6, 1);
        var monthEnd = new DateTime(2024, 6, 30);

        using var ctx = new ShardedDbContext(_shardedOptions);
        return ctx.Transactions
            .Where(t => t.AccountNumber == accountNumber &&
                        t.TransactionDate >= monthStart &&
                        t.TransactionDate <= monthEnd)
            .Count();
    }

    #endregion

    #region Insert Performance by Date

    /// <summary>
    /// Single table: Insert new transactions.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Insert")]
    public int SingleTable_InsertTransactions()
    {
        var transactions = Enumerable.Range(0, 100)
            .Select(i => new Transaction
            {
                TransactionRef = $"TX_{Guid.NewGuid():N}",
                AccountNumber = "ACC12345",
                TransactionDate = DateTime.UtcNow,
                Amount = 100m * i,
                Type = TransactionType.Credit,
                Status = "Completed",
                CreatedAt = DateTime.UtcNow
            }).ToList();

        using var ctx = new SingleTableDbContext(_singleOptions);
        ctx.Transactions.AddRange(transactions);
        return ctx.SaveChanges();
    }

    /// <summary>
    /// Sharded table: Insert new transactions (same month - single shard).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Insert")]
    public int Sharded_InsertTransactions_SameMonth()
    {
        var currentDate = DateTime.UtcNow;
        var transactions = Enumerable.Range(0, 100)
            .Select(i => new ShardedTransaction
            {
                TransactionRef = $"TX_{Guid.NewGuid():N}",
                AccountNumber = "ACC12345",
                TransactionDate = currentDate, // All same month
                Amount = 100m * i,
                Type = TransactionType.Credit,
                Status = "Completed",
                CreatedAt = DateTime.UtcNow
            }).ToList();

        using var ctx = new ShardedDbContext(_shardedOptions);
        ctx.Transactions.AddRange(transactions);
        return ctx.SaveChanges();
    }

    /// <summary>
    /// Sharded table: Insert transactions across multiple months.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Insert")]
    public int Sharded_InsertTransactions_MultipleMonths()
    {
        var transactions = Enumerable.Range(0, 100)
            .Select(i => new ShardedTransaction
            {
                TransactionRef = $"TX_{Guid.NewGuid():N}",
                AccountNumber = "ACC12345",
                TransactionDate = _startDate.AddMonths(i % 12), // Distributed across 12 months
                Amount = 100m * i,
                Type = TransactionType.Credit,
                Status = "Completed",
                CreatedAt = DateTime.UtcNow
            }).ToList();

        using var ctx = new ShardedDbContext(_shardedOptions);
        ctx.Transactions.AddRange(transactions);
        return ctx.SaveChanges();
    }

    #endregion

    #region Rolling Window Queries

    /// <summary>
    /// Single table: Query last 30 days transactions.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("RollingWindow")]
    public int SingleTable_QueryLast30Days()
    {
        var endDate = _endDate;
        var startDate = endDate.AddDays(-30);

        using var ctx = new SingleTableDbContext(_singleOptions);
        return ctx.Transactions
            .Where(t => t.TransactionDate >= startDate && t.TransactionDate <= endDate)
            .Count();
    }

    /// <summary>
    /// Indexed table: Query last 30 days transactions.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("RollingWindow")]
    public int Indexed_QueryLast30Days()
    {
        var endDate = _endDate;
        var startDate = endDate.AddDays(-30);

        using var ctx = new IndexedSingleTableDbContext(_indexedOptions);
        return ctx.Transactions
            .Where(t => t.TransactionDate >= startDate && t.TransactionDate <= endDate)
            .Count();
    }

    /// <summary>
    /// Sharded table: Query last 30 days (may span 2 monthly shards).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("RollingWindow")]
    public int Sharded_QueryLast30Days()
    {
        var endDate = _endDate;
        var startDate = endDate.AddDays(-30);

        using var ctx = new ShardedDbContext(_shardedOptions);
        return ctx.Transactions
            .Where(t => t.TransactionDate >= startDate && t.TransactionDate <= endDate)
            .Count();
    }

    /// <summary>
    /// Single table: Query last 90 days transactions.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("RollingWindow")]
    public int SingleTable_QueryLast90Days()
    {
        var endDate = _endDate;
        var startDate = endDate.AddDays(-90);

        using var ctx = new SingleTableDbContext(_singleOptions);
        return ctx.Transactions
            .Where(t => t.TransactionDate >= startDate && t.TransactionDate <= endDate)
            .Count();
    }

    /// <summary>
    /// Sharded table: Query last 90 days (may span 3-4 monthly shards).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("RollingWindow")]
    public int Sharded_QueryLast90Days()
    {
        var endDate = _endDate;
        var startDate = endDate.AddDays(-90);

        using var ctx = new ShardedDbContext(_shardedOptions);
        return ctx.Transactions
            .Where(t => t.TransactionDate >= startDate && t.TransactionDate <= endDate)
            .Count();
    }

    #endregion

    #region Year-over-Year Comparison

    /// <summary>
    /// Single table: Compare same month across different periods.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("YearOverYear")]
    public decimal SingleTable_CompareMonths()
    {
        var month1Start = new DateTime(2024, 1, 1);
        var month1End = new DateTime(2024, 1, 31);
        var month2Start = new DateTime(2024, 7, 1);
        var month2End = new DateTime(2024, 7, 31);

        using var ctx = new SingleTableDbContext(_singleOptions);

        var sum1 = ctx.Transactions
            .Where(t => t.TransactionDate >= month1Start && t.TransactionDate <= month1End)
            .Sum(t => t.Amount);

        var sum2 = ctx.Transactions
            .Where(t => t.TransactionDate >= month2Start && t.TransactionDate <= month2End)
            .Sum(t => t.Amount);

        return sum2 - sum1;
    }

    /// <summary>
    /// Sharded table: Compare same month across different periods (parallel shard queries).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("YearOverYear")]
    public decimal Sharded_CompareMonths()
    {
        var month1Start = new DateTime(2024, 1, 1);
        var month1End = new DateTime(2024, 1, 31);
        var month2Start = new DateTime(2024, 7, 1);
        var month2End = new DateTime(2024, 7, 31);

        using var ctx = new ShardedDbContext(_shardedOptions);

        var sum1 = ctx.Transactions
            .Where(t => t.TransactionDate >= month1Start && t.TransactionDate <= month1End)
            .Sum(t => t.Amount);

        var sum2 = ctx.Transactions
            .Where(t => t.TransactionDate >= month2Start && t.TransactionDate <= month2End)
            .Sum(t => t.Amount);

        return sum2 - sum1;
    }

    #endregion
}
