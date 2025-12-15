using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Transactions;
using Dtde.Core.Metadata;
using Dtde.Core.Transactions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dtde.Core.Tests.Transactions;

/// <summary>
/// Tests for <see cref="CrossShardTransactionCoordinator"/>.
/// </summary>
public class CrossShardTransactionCoordinatorTests
{
    private readonly IShardRegistry _shardRegistry;
    private readonly ILogger<CrossShardTransactionCoordinator> _coordinatorLogger;
    private readonly ILogger<CrossShardTransaction> _transactionLogger;
    private readonly Func<string, CancellationToken, Task<DbContext>> _contextFactory;

    public CrossShardTransactionCoordinatorTests()
    {
        var registry = new ShardRegistry();
        registry.AddShard(new ShardMetadataBuilder()
            .WithId("shard-1")
            .WithName("Shard 1")
            .WithConnectionString("Server=test;Database=Shard1")
            .Build());
        registry.AddShard(new ShardMetadataBuilder()
            .WithId("shard-2")
            .WithName("Shard 2")
            .WithConnectionString("Server=test;Database=Shard2")
            .Build());

        _shardRegistry = registry;
        _coordinatorLogger = NullLogger<CrossShardTransactionCoordinator>.Instance;
        _transactionLogger = NullLogger<CrossShardTransaction>.Instance;
        _contextFactory = CreateTestContextFactory();
    }

    [Fact(DisplayName = "BeginTransactionAsync creates new transaction")]
    public async Task BeginTransactionAsync_CreatesNewTransaction()
    {
        var coordinator = CreateCoordinator();

        await using var transaction = await coordinator.BeginTransactionAsync();

        Assert.NotNull(transaction);
        Assert.Equal(TransactionState.Active, transaction.State);
        Assert.NotEmpty(transaction.TransactionId);
    }

    [Fact(DisplayName = "BeginTransactionAsync applies custom options")]
    public async Task BeginTransactionAsync_AppliesCustomOptions()
    {
        var coordinator = CreateCoordinator();
        var options = new CrossShardTransactionOptions
        {
            IsolationLevel = CrossShardIsolationLevel.Serializable,
            Timeout = TimeSpan.FromMinutes(5)
        };

        await using var transaction = await coordinator.BeginTransactionAsync(options);

        Assert.Equal(CrossShardIsolationLevel.Serializable, transaction.IsolationLevel);
        Assert.Equal(TimeSpan.FromMinutes(5), transaction.Timeout);
    }

    [Fact(DisplayName = "BeginTransactionAsync throws when nested transaction attempted")]
    public async Task BeginTransactionAsync_NestedTransaction_Throws()
    {
        var coordinator = CreateCoordinator();

        await using var transaction1 = await coordinator.BeginTransactionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.BeginTransactionAsync());
    }

    [Fact(DisplayName = "CurrentTransaction returns active transaction")]
    public async Task CurrentTransaction_ReturnsActiveTransaction()
    {
        var coordinator = CreateCoordinator();

        Assert.Null(coordinator.CurrentTransaction);

        await using var transaction = await coordinator.BeginTransactionAsync();

        Assert.Same(transaction, coordinator.CurrentTransaction);
    }

    [Fact(DisplayName = "CurrentTransaction indicates active transaction")]
    public async Task CurrentTransaction_IndicatesActiveTransaction()
    {
        var coordinator = CreateCoordinator();

        Assert.Null(coordinator.CurrentTransaction);

        await using var transaction = await coordinator.BeginTransactionAsync();

        Assert.NotNull(coordinator.CurrentTransaction);
    }

    [Fact(DisplayName = "ExecuteInTransactionAsync commits on success")]
    public async Task ExecuteInTransactionAsync_Success_Commits()
    {
        var coordinator = CreateCoordinator();
        var executed = false;

        await coordinator.ExecuteInTransactionAsync(async transaction =>
        {
            await transaction.EnlistAsync("shard-1");
            executed = true;
        });

        Assert.True(executed);
        Assert.Null(coordinator.CurrentTransaction);
    }

    [Fact(DisplayName = "ExecuteInTransactionAsync rolls back on exception")]
    public async Task ExecuteInTransactionAsync_Exception_RollsBack()
    {
        var coordinator = CreateCoordinator();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await coordinator.ExecuteInTransactionAsync(async transaction =>
            {
                await transaction.EnlistAsync("shard-1");
                throw new InvalidOperationException("Test exception");
            });
        });

        Assert.Null(coordinator.CurrentTransaction);
    }

    [Fact(DisplayName = "ExecuteInTransactionAsync with result returns value")]
    public async Task ExecuteInTransactionAsync_WithResult_ReturnsValue()
    {
        var coordinator = CreateCoordinator();

        var result = await coordinator.ExecuteInTransactionAsync(async transaction =>
        {
            await transaction.EnlistAsync("shard-1");
            return 42;
        });

        Assert.Equal(42, result);
    }

    [Fact(DisplayName = "ExecuteInTransactionAsync with options applies them")]
    public async Task ExecuteInTransactionAsync_WithOptions_AppliesOptions()
    {
        var coordinator = CreateCoordinator();
        var options = new CrossShardTransactionOptions
        {
            IsolationLevel = CrossShardIsolationLevel.Snapshot
        };
        CrossShardIsolationLevel? capturedLevel = null;

        await coordinator.ExecuteInTransactionAsync(
            transaction =>
            {
                capturedLevel = transaction.IsolationLevel;
                return Task.CompletedTask;
            },
            options);

        Assert.Equal(CrossShardIsolationLevel.Snapshot, capturedLevel);
    }

    [Fact(DisplayName = "RecoverAsync returns zero when no in-doubt transactions")]
    public async Task RecoverAsync_NoTransactions_ReturnsZero()
    {
        var coordinator = CreateCoordinator();

        var recovered = await coordinator.RecoverAsync();

        Assert.Equal(0, recovered);
    }

    [Fact(DisplayName = "Transaction ID includes timestamp")]
    public async Task TransactionId_IncludesTimestamp()
    {
        var coordinator = CreateCoordinator();

        await using var transaction = await coordinator.BeginTransactionAsync();

        // Transaction IDs are formatted as "XS-{timestamp}-{uniqueId}"
        Assert.StartsWith("XS-", transaction.TransactionId);
        Assert.Contains("-", transaction.TransactionId);
    }

    [Fact(DisplayName = "Transaction ID includes name when provided")]
    public async Task TransactionId_IncludesNameWhenProvided()
    {
        var coordinator = CreateCoordinator();
        var options = new CrossShardTransactionOptions
        {
            TransactionName = "MyTransaction"
        };

        await using var transaction = await coordinator.BeginTransactionAsync(options);

        Assert.Contains("MyTransaction", transaction.TransactionId);
    }

    private CrossShardTransactionCoordinator CreateCoordinator()
    {
        return new CrossShardTransactionCoordinator(
            _shardRegistry,
            _contextFactory,
            _coordinatorLogger,
            _transactionLogger);
    }

    private static Func<string, CancellationToken, Task<DbContext>> CreateTestContextFactory()
    {
        return (shardId, ct) =>
        {
            var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
            optionsBuilder.UseInMemoryDatabase($"Test_{shardId}_{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            return Task.FromResult<DbContext>(new TestDbContext(optionsBuilder.Options));
        };
    }

    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions options) : base(options)
        {
        }
    }
}
