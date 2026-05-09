using System.Data.Common;

using Dtde.Abstractions.Transactions;
using Dtde.Core.Transactions;
using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dtde.Integration.Tests.Transactions;

/// <summary>
/// End-to-end tests for the transaction-depth features:
/// <list type="bullet">
///   <item><description>Savepoints (within-shard partial rollback).</description></item>
///   <item><description>Read-after-write within an ambient cross-shard transaction.</description></item>
///   <item><description>Crash-recovery transaction log with <c>RecoverAsync</c>.</description></item>
/// </list>
/// </summary>
public class TransactionDepthIntegrationTests : IAsyncLifetime
{
    private readonly List<DbConnection> _connections = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var c in _connections)
        {
            await c.DisposeAsync();
        }
    }

    [Fact(DisplayName = "Savepoint: rollback to savepoint discards work since the savepoint, transaction stays open")]
    public async Task Savepoint_RollbackToSavepoint_DiscardsLaterWork()
    {
        var (sp, scope, ctx, euAnchor, _) = await BuildTwoShardContextAsync();
        await using (sp)
        await using (scope)
        {
            await using (var tx = await ctx.BeginCrossShardTransactionAsync())
            {
                var crossShardTx = (CrossShardTransaction)tx;

                var euShard = ctx.ShardRegistry.GetShard("EU")!;
                var euParticipant = await crossShardTx.GetOrCreateParticipantAsync(euShard);

                // First write: pre-savepoint.
                euParticipant.Context.Set<DepthEntity>().Add(new DepthEntity { Id = 1, Region = "EU", Note = "before" });
                await euParticipant.Context.SaveChangesAsync();

                // Create savepoint, then write more.
                await euParticipant.CreateSavepointAsync("sp1");

                euParticipant.Context.Set<DepthEntity>().Add(new DepthEntity { Id = 2, Region = "EU", Note = "after" });
                await euParticipant.Context.SaveChangesAsync();

                // Roll back to the savepoint — second write disappears, first
                // write survives.
                await euParticipant.RollbackToSavepointAsync("sp1");

                await tx.CommitAsync();
            }

            // Verify outside the transaction scope so the participant's
            // connection is fully disposed and the SQLite shared-cache lock
            // is released.
            Assert.Equal(1, await CountRowsAsync(euAnchor, "DepthEntities"));
        }
    }

    [Fact(DisplayName = "Read-after-write: queries inside a transaction see uncommitted writes on the same shard")]
    public async Task ReadAfterWrite_QueriesSeeUncommittedWrites()
    {
        var (sp, scope, ctx, _, _) = await BuildTwoShardContextAsync();
        await using (sp)
        await using (scope)
        {
            await using (var tx = await ctx.BeginCrossShardTransactionAsync())
            {
                var crossShardTx = (CrossShardTransaction)tx;
                var euShard = ctx.ShardRegistry.GetShard("EU")!;

                var euParticipant = await crossShardTx.GetOrCreateParticipantAsync(euShard);
                euParticipant.Context.Set<DepthEntity>().Add(new DepthEntity
                {
                    Id = 99,
                    Region = "EU",
                    Note = "inside-tx",
                });
                await euParticipant.Context.SaveChangesAsync();

                // Query through the parent context's executor — should see
                // the uncommitted row because the executor reuses the
                // participant's open context.
                var executor = scope.ServiceProvider.GetRequiredService<Dtde.EntityFramework.Query.IShardedQueryExecutor>();
                var results = await executor.ExecuteAsync(
                    ctx.Set<DepthEntity>().Where(e => e.Region == "EU").AsQueryable());

                Assert.Single(results);
                Assert.Equal("inside-tx", results[0].Note);

                await tx.CommitAsync();
            }
        }
    }

    [Fact(DisplayName = "RecoverAsync: in-doubt transaction with all-prepared participants is resolved as committed")]
    public async Task RecoverAsync_FullyPreparedTransaction_ResolvedCommitted()
    {
        var log = new InMemoryTransactionLog();

        await log.RecordTransactionStartedAsync("tx-recover-1", CrossShardTransactionOptions.Default);
        await log.RecordParticipantEnlistedAsync("tx-recover-1", "EU");
        await log.RecordParticipantEnlistedAsync("tx-recover-1", "US");
        await log.RecordParticipantPreparedAsync("tx-recover-1", "EU");
        await log.RecordParticipantPreparedAsync("tx-recover-1", "US");
        // The "coordinator crashed" between Prepare and Commit.

        var coordinator = BuildIsolatedCoordinator(log);

        var resolved = await coordinator.RecoverAsync();
        Assert.Equal(1, resolved);

        // After recovery, the transaction is no longer in-doubt.
        var still = await log.GetInDoubtTransactionsAsync();
        Assert.Empty(still);
    }

    [Fact(DisplayName = "RecoverAsync: in-doubt transaction with partial prepares is rolled back")]
    public async Task RecoverAsync_PartiallyPreparedTransaction_ResolvedRolledBack()
    {
        var log = new InMemoryTransactionLog();

        await log.RecordTransactionStartedAsync("tx-recover-2", CrossShardTransactionOptions.Default);
        await log.RecordParticipantEnlistedAsync("tx-recover-2", "EU");
        await log.RecordParticipantEnlistedAsync("tx-recover-2", "US");
        await log.RecordParticipantPreparedAsync("tx-recover-2", "EU");
        // US never prepared — recovery must roll back.

        var coordinator = BuildIsolatedCoordinator(log);
        var resolved = await coordinator.RecoverAsync();
        Assert.Equal(1, resolved);

        var still = await log.GetInDoubtTransactionsAsync();
        Assert.Empty(still);
    }

    /// <summary>
    /// Builds a coordinator wired to <paramref name="log"/> with a stub
    /// participant factory. No DbContext, no interceptors — keeps the
    /// recovery tests focused on the coordinator/log interaction without
    /// schema-creation transactions polluting the log.
    /// </summary>
    private static CrossShardTransactionCoordinator BuildIsolatedCoordinator(ITransactionLog log)
    {
        var registry = new Dtde.Core.Metadata.ShardRegistry();
        registry.AddShard(new Dtde.Core.Metadata.ShardMetadataBuilder()
            .WithId("EU")
            .WithName("EU")
            .Build());
        registry.AddShard(new Dtde.Core.Metadata.ShardMetadataBuilder()
            .WithId("US")
            .WithName("US")
            .Build());

        ShardParticipantFactory factory = (shardId, isolationLevel, ct) =>
            throw new InvalidOperationException(
                "RecoverAsync should not need to create participants for already-finalised transactions.");

        return new CrossShardTransactionCoordinator(
            registry,
            factory,
            log,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CrossShardTransactionCoordinator>.Instance,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CrossShardTransaction>.Instance);
    }

    private async Task<(ServiceProvider sp, AsyncServiceScope scope, DepthDbContext ctx, SqliteConnection euAnchor, SqliteConnection usAnchor)>
        BuildTwoShardContextAsync()
    {
        return await BuildTwoShardContextWithLogAsync(transactionLog: null);
    }

    private async Task<(ServiceProvider sp, AsyncServiceScope scope, DepthDbContext ctx, SqliteConnection euAnchor, SqliteConnection usAnchor)>
        BuildTwoShardContextWithLogAsync(ITransactionLog? transactionLog)
    {
        var dbId = Guid.NewGuid().ToString("N");
        var euConn = $"Data Source=eu_depth_{dbId};Mode=Memory;Cache=Shared";
        var usConn = $"Data Source=us_depth_{dbId};Mode=Memory;Cache=Shared";

        var euAnchor = new SqliteConnection(euConn);
        var usAnchor = new SqliteConnection(usConn);
        await euAnchor.OpenAsync();
        await usAnchor.OpenAsync();
        _connections.Add(euAnchor);
        _connections.Add(usAnchor);

        var services = new ServiceCollection();
        services.AddLogging();
        if (transactionLog is not null)
        {
            services.AddSingleton(transactionLog);
        }
        services.AddDtdeDbContext<DepthDbContext>(
            (db, conn) => db.UseSqlite(conn ?? "Data Source=:memory:"),
            dtde => dtde
                .AddShard("EU", euConn)
                .AddShard("US", usConn));

        var sp = services.BuildServiceProvider();
        var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<DepthDbContext>();
        await ctx.EnsureAllShardsCreatedAsync();

        return (sp, scope, ctx, euAnchor, usAnchor);
    }

#pragma warning disable CA2100
    private static async Task<long> CountRowsAsync(DbConnection connection, string tableName)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{tableName}\"";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }
#pragma warning restore CA2100
}

#pragma warning disable CA1062

public class DepthEntity
{
    public int Id { get; set; }
    public string Region { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

public class DepthDbContext : DtdeDbContext
{
    public DbSet<DepthEntity> DepthEntities => Set<DepthEntity>();

    public DepthDbContext(DbContextOptions<DepthDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DepthEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Region).HasMaxLength(10).IsRequired();
            e.Property(x => x.Note).HasMaxLength(100);
            e.ShardBy(x => x.Region);
        });
    }
}

#pragma warning restore CA1062
