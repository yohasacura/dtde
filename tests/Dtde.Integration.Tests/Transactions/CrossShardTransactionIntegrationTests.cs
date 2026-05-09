using System.Data.Common;

using Dtde.Abstractions.Transactions;
using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dtde.Integration.Tests.Transactions;

/// <summary>
/// End-to-end tests for the public cross-shard transaction surface:
/// <list type="bullet">
///   <item><description>The <c>BeginCrossShardTransactionAsync</c> extension on <see cref="DtdeDbContext"/>.</description></item>
///   <item><description>Atomic 2PC commit across multiple shards.</description></item>
///   <item><description>Roll-back on failure.</description></item>
///   <item><description>The single-shard fast path (no 2PC overhead when only one shard is enlisted).</description></item>
///   <item><description>Group-qualified participant ids — same local id in different groups stays isolated.</description></item>
/// </list>
/// </summary>
public class CrossShardTransactionIntegrationTests : IAsyncLifetime
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

    [Fact(DisplayName = "BeginCrossShardTransactionAsync commits atomically across two shards")]
    public async Task BeginCrossShardTransaction_CommitsAtomicallyAcrossTwoShards()
    {
        var (sp, scope, ctx, euAnchor, usAnchor) = await BuildTwoDbContextAsync();
        await using (sp)
        await using (scope)
        {
            await using (var tx = await ctx.BeginCrossShardTransactionAsync())
            {
                var euShard = ctx.ShardRegistry.GetShard("EU")!;
                var usShard = ctx.ShardRegistry.GetShard("US")!;

                var euParticipant = await ((Dtde.Core.Transactions.CrossShardTransaction)tx)
                    .GetOrCreateParticipantAsync(euShard);
                var usParticipant = await ((Dtde.Core.Transactions.CrossShardTransaction)tx)
                    .GetOrCreateParticipantAsync(usShard);

                euParticipant.Context.Set<TxCustomer>().Add(new TxCustomer { Id = 1, Name = "Alice", Region = "EU" });
                usParticipant.Context.Set<TxCustomer>().Add(new TxCustomer { Id = 2, Name = "Bob", Region = "US" });

                await tx.CommitAsync();

                Assert.Equal(TransactionState.Committed, tx.State);
            }

            Assert.Equal(1, await CountRowsAsync(euAnchor, "TxCustomers"));
            Assert.Equal(1, await CountRowsAsync(usAnchor, "TxCustomers"));
        }
    }

    [Fact(DisplayName = "BeginCrossShardTransactionAsync rolls back both shards on failure")]
    public async Task BeginCrossShardTransaction_RollsBackBothShardsOnFailure()
    {
        var (sp, scope, ctx, euAnchor, usAnchor) = await BuildTwoDbContextAsync();
        await using (sp)
        await using (scope)
        {
            try
            {
                await using var tx = await ctx.BeginCrossShardTransactionAsync();

                var euShard = ctx.ShardRegistry.GetShard("EU")!;
                var usShard = ctx.ShardRegistry.GetShard("US")!;

                var euParticipant = await ((Dtde.Core.Transactions.CrossShardTransaction)tx)
                    .GetOrCreateParticipantAsync(euShard);
                var usParticipant = await ((Dtde.Core.Transactions.CrossShardTransaction)tx)
                    .GetOrCreateParticipantAsync(usShard);

                euParticipant.Context.Set<TxCustomer>().Add(new TxCustomer { Id = 1, Name = "Alice", Region = "EU" });
                usParticipant.Context.Set<TxCustomer>().Add(new TxCustomer { Id = 2, Name = "Bob", Region = "US" });

                throw new InvalidOperationException("simulated failure");
            }
            catch (InvalidOperationException ex) when (ex.Message == "simulated failure")
            {
                // Expected — DisposeAsync rolls back the transaction.
            }

            // Both shards should be empty: rollback fired on dispose.
            Assert.Equal(0, await CountRowsAsync(euAnchor, "TxCustomers"));
            Assert.Equal(0, await CountRowsAsync(usAnchor, "TxCustomers"));
        }
    }

    [Fact(DisplayName = "Single-shard fast path commits without prepare phase")]
    public async Task SingleShard_FastPath_CommitsCorrectly()
    {
        var (sp, scope, ctx, euAnchor, _) = await BuildTwoDbContextAsync();
        await using (sp)
        await using (scope)
        {
            await using (var tx = await ctx.BeginCrossShardTransactionAsync())
            {
                var euShard = ctx.ShardRegistry.GetShard("EU")!;

                // Only enlist EU. The coordinator should detect single-shard
                // and bypass the 2PC prepare phase.
                var euParticipant = await ((Dtde.Core.Transactions.CrossShardTransaction)tx)
                    .GetOrCreateParticipantAsync(euShard);

                euParticipant.Context.Set<TxCustomer>().Add(new TxCustomer { Id = 1, Name = "Alice", Region = "EU" });

                Assert.Single(tx.EnlistedShards);

                await tx.CommitAsync();
                Assert.Equal(TransactionState.Committed, tx.State);
            }

            Assert.Equal(1, await CountRowsAsync(euAnchor, "TxCustomers"));
        }
    }

    [Fact(DisplayName = "Group-qualified ids: same local shard id in two groups doesn't alias as one participant")]
    public async Task QualifiedIds_SameLocalIdInTwoGroups_StaysIsolated()
    {
        // Two groups, both with a shard literally called "0". The transaction
        // must enlist them as distinct participants — qualified ids prevent
        // collision.
        var dbId = Guid.NewGuid().ToString("N");
        var groupAConn = $"Data Source=ga_{dbId};Mode=Memory;Cache=Shared";
        var groupBConn = $"Data Source=gb_{dbId};Mode=Memory;Cache=Shared";

        var aAnchor = new SqliteConnection(groupAConn);
        var bAnchor = new SqliteConnection(groupBConn);
        await aAnchor.OpenAsync();
        await bAnchor.OpenAsync();
        _connections.Add(aAnchor);
        _connections.Add(bAnchor);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDtdeDbContext<TwoGroupTxDbContext>(
            (db, conn) => db.UseSqlite(conn ?? "Data Source=:memory:"),
            dtde => dtde
                .AddShardGroup("groupA", g => g.AddTableShardInDatabase("0", groupAConn))
                .AddShardGroup("groupB", g => g.AddTableShardInDatabase("0", groupBConn)));

        var sp = services.BuildServiceProvider();
        var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TwoGroupTxDbContext>();
        await ctx.EnsureAllShardsCreatedAsync();

        await using (sp)
        await using (scope)
        {
            await using (var tx = await ctx.BeginCrossShardTransactionAsync())
            {
                var crossShardTx = (Dtde.Core.Transactions.CrossShardTransaction)tx;
                var aShard = ctx.ShardRegistry.GetShard("groupA::0")!;
                var bShard = ctx.ShardRegistry.GetShard("groupB::0")!;

                var aParticipant = await crossShardTx.GetOrCreateParticipantAsync(aShard);
                var bParticipant = await crossShardTx.GetOrCreateParticipantAsync(bShard);

                Assert.NotSame(aParticipant, bParticipant);
                Assert.Equal(2, tx.EnlistedShards.Count);

                aParticipant.Context.Set<TxAEntity>().Add(new TxAEntity { Id = 1, Name = "from-A" });
                bParticipant.Context.Set<TxBEntity>().Add(new TxBEntity { Id = 1, Name = "from-B" });

                await tx.CommitAsync();
            }

            Assert.Equal(1, await CountRowsAsync(aAnchor, "TxAEntities_0"));
            Assert.Equal(1, await CountRowsAsync(bAnchor, "TxBEntities_0"));
        }
    }

    private async Task<(ServiceProvider sp, AsyncServiceScope scope, TwoShardTxDbContext ctx, SqliteConnection euAnchor, SqliteConnection usAnchor)>
        BuildTwoDbContextAsync()
    {
        var dbId = Guid.NewGuid().ToString("N");
        var euConn = $"Data Source=eu_tx_{dbId};Mode=Memory;Cache=Shared";
        var usConn = $"Data Source=us_tx_{dbId};Mode=Memory;Cache=Shared";

        var euAnchor = new SqliteConnection(euConn);
        var usAnchor = new SqliteConnection(usConn);
        await euAnchor.OpenAsync();
        await usAnchor.OpenAsync();
        _connections.Add(euAnchor);
        _connections.Add(usAnchor);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDtdeDbContext<TwoShardTxDbContext>(
            (db, conn) => db.UseSqlite(conn ?? "Data Source=:memory:"),
            dtde => dtde
                .AddShard("EU", euConn)
                .AddShard("US", usConn));

        var sp = services.BuildServiceProvider();
        var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TwoShardTxDbContext>();
        await ctx.EnsureAllShardsCreatedAsync();

        return (sp, scope, ctx, euAnchor, usAnchor);
    }

#pragma warning disable CA2100 // tableName comes from test-fixture identifiers, not user input.
    private static async Task<long> CountRowsAsync(DbConnection connection, string tableName)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{tableName}\"";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }
#pragma warning restore CA2100
}

#pragma warning disable CA1062 // Test fixture: modelBuilder null check is unnecessary noise.

public class TxCustomer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
}

public class TwoShardTxDbContext : DtdeDbContext
{
    public DbSet<TxCustomer> TxCustomers => Set<TxCustomer>();

    public TwoShardTxDbContext(DbContextOptions<TwoShardTxDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TxCustomer>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Region).HasMaxLength(10).IsRequired();
            e.ShardBy(c => c.Region);
        });
    }
}

public class TxAEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class TxBEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class TwoGroupTxDbContext : DtdeDbContext
{
    public DbSet<TxAEntity> TxAEntities => Set<TxAEntity>();
    public DbSet<TxBEntity> TxBEntities => Set<TxBEntity>();

    public TwoGroupTxDbContext(DbContextOptions<TwoGroupTxDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TxAEntity>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(50).IsRequired();
            e.ShardBy(c => c.Name).UseShardGroup("groupA");
        });

        modelBuilder.Entity<TxBEntity>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(50).IsRequired();
            e.ShardBy(c => c.Name).UseShardGroup("groupB");
        });
    }
}

#pragma warning restore CA1062
