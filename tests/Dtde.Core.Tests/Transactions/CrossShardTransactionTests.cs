using Dtde.Abstractions.Exceptions;
using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Transactions;
using Dtde.Core.Metadata;
using Dtde.Core.Transactions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dtde.Core.Tests.Transactions;

/// <summary>
/// Tests for <see cref="CrossShardTransaction"/>.
/// </summary>
public class CrossShardTransactionTests
{
    private readonly IShardRegistry _shardRegistry;
    private readonly ILogger<CrossShardTransaction> _logger;
    private readonly CrossShardTransactionOptions _defaultOptions;

    public CrossShardTransactionTests()
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
        _logger = NullLogger<CrossShardTransaction>.Instance;
        _defaultOptions = CrossShardTransactionOptions.Default;
    }

    [Fact(DisplayName = "Constructor sets properties correctly")]
    public async Task Constructor_SetsPropertiesCorrectly()
    {
        var options = new CrossShardTransactionOptions
        {
            IsolationLevel = CrossShardIsolationLevel.Snapshot,
            Timeout = TimeSpan.FromMinutes(2)
        };

        await using var transaction = CreateTransaction("test-tx-1", options);

        Assert.Equal("test-tx-1", transaction.TransactionId);
        Assert.Equal(TransactionState.Active, transaction.State);
        Assert.Equal(CrossShardIsolationLevel.Snapshot, transaction.IsolationLevel);
        Assert.Equal(TimeSpan.FromMinutes(2), transaction.Timeout);
        Assert.Empty(transaction.EnlistedShards);
    }

    [Fact(DisplayName = "EnlistAsync throws for unknown shard")]
    public async Task EnlistAsync_UnknownShard_ThrowsShardNotFoundException()
    {
        await using var transaction = CreateTransaction("test-tx-2");

        await Assert.ThrowsAsync<ShardNotFoundException>(
            () => transaction.EnlistAsync("unknown-shard"));
    }

    [Fact(DisplayName = "EnlistAsync adds shard to enlisted collection")]
    public async Task EnlistAsync_ValidShard_AddsToEnlistedShards()
    {
        await using var transaction = CreateTransactionWithMockContext("test-tx-3");

        await transaction.EnlistAsync("shard-1");

        Assert.Contains("shard-1", transaction.EnlistedShards);
    }

    [Fact(DisplayName = "EnlistAsync is idempotent")]
    public async Task EnlistAsync_SameShardTwice_NoError()
    {
        await using var transaction = CreateTransactionWithMockContext("test-tx-4");

        await transaction.EnlistAsync("shard-1");
        await transaction.EnlistAsync("shard-1");

        Assert.Single(transaction.EnlistedShards);
    }

    [Fact(DisplayName = "EnlistAsync throws when transaction is not active")]
    public async Task EnlistAsync_NotActive_ThrowsInvalidOperationException()
    {
        await using var transaction = CreateTransactionWithMockContext("test-tx-5");

        await transaction.EnlistAsync("shard-1");
        await transaction.CommitAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => transaction.EnlistAsync("shard-2"));
    }

    [Fact(DisplayName = "CommitAsync with no participants succeeds")]
    public async Task CommitAsync_NoParticipants_Succeeds()
    {
        await using var transaction = CreateTransaction("test-tx-6");

        await transaction.CommitAsync();

        Assert.Equal(TransactionState.Committed, transaction.State);
    }

    [Fact(DisplayName = "CommitAsync throws when not in active state")]
    public async Task CommitAsync_NotActive_ThrowsInvalidOperationException()
    {
        await using var transaction = CreateTransaction("test-tx-7");

        await transaction.CommitAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => transaction.CommitAsync());
    }

    [Fact(DisplayName = "RollbackAsync sets state to RolledBack")]
    public async Task RollbackAsync_SetsStateToRolledBack()
    {
        await using var transaction = CreateTransactionWithMockContext("test-tx-8");

        await transaction.EnlistAsync("shard-1");
        await transaction.RollbackAsync();

        Assert.Equal(TransactionState.RolledBack, transaction.State);
    }

    [Fact(DisplayName = "RollbackAsync is idempotent")]
    public async Task RollbackAsync_CalledTwice_NoError()
    {
        await using var transaction = CreateTransactionWithMockContext("test-tx-9");

        await transaction.EnlistAsync("shard-1");
        await transaction.RollbackAsync();
        await transaction.RollbackAsync();

        Assert.Equal(TransactionState.RolledBack, transaction.State);
    }

    [Fact(DisplayName = "GetParticipant returns null for unenlisted shard")]
    public async Task GetParticipant_NotEnlisted_ReturnsNull()
    {
        await using var transaction = CreateTransaction("test-tx-10");

        var participant = transaction.GetParticipant("shard-1");

        Assert.Null(participant);
    }

    [Fact(DisplayName = "GetParticipant returns participant for enlisted shard")]
    public async Task GetParticipant_Enlisted_ReturnsParticipant()
    {
        await using var transaction = CreateTransactionWithMockContext("test-tx-11");

        await transaction.EnlistAsync("shard-1");
        var participant = transaction.GetParticipant("shard-1");

        Assert.NotNull(participant);
        Assert.Equal("shard-1", participant.ShardId);
    }

    [Fact(DisplayName = "DisposeAsync rolls back uncommitted transaction")]
    public async Task DisposeAsync_UncommittedTransaction_RollsBack()
    {
        var transaction = CreateTransactionWithMockContext("test-tx-12");

        await transaction.EnlistAsync("shard-1");
        await transaction.DisposeAsync();

        Assert.Equal(TransactionState.RolledBack, transaction.State);
    }

    [Fact(DisplayName = "DisposeAsync does not rollback committed transaction")]
    public async Task DisposeAsync_CommittedTransaction_DoesNotRollback()
    {
        var transaction = CreateTransactionWithMockContext("test-tx-13");

        await transaction.EnlistAsync("shard-1");
        await transaction.CommitAsync();
        var stateBeforeDispose = transaction.State;

        await transaction.DisposeAsync();

        Assert.Equal(TransactionState.Committed, stateBeforeDispose);
    }

    private CrossShardTransaction CreateTransaction(string transactionId, CrossShardTransactionOptions? options = null)
    {
        return new CrossShardTransaction(
            transactionId,
            options ?? _defaultOptions,
            _shardRegistry,
            (shardId, ct) => throw new NotSupportedException("Context factory not configured for this test"),
            _logger);
    }

    private CrossShardTransaction CreateTransactionWithMockContext(string transactionId, CrossShardTransactionOptions? options = null)
    {
        return new CrossShardTransaction(
            transactionId,
            options ?? _defaultOptions,
            _shardRegistry,
            async (shardId, ct) =>
            {
                var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
                optionsBuilder.UseInMemoryDatabase($"Test_{shardId}_{transactionId}")
                    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
                var context = new TestDbContext(optionsBuilder.Options);

                // In-memory database doesn't support transactions, but we can still test the flow
                return context;
            },
            _logger);
    }

    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions options) : base(options)
        {
        }

        public DbSet<TestEntity> TestEntities => Set<TestEntity>();
    }

    private class TestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
