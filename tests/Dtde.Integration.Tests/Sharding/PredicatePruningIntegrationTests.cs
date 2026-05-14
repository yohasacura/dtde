using System.Data.Common;

using Dtde.Core.Transactions;
using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
using Dtde.EntityFramework.Query;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dtde.Integration.Tests.Sharding;

/// <summary>
/// End-to-end tests for query-time shard pruning via <c>Where</c> predicates.
///
/// <para>
/// The interesting case is the closure-captured shard-key value
/// (<c>var r = "EU"; ... Where(a => a.Region == r)</c>) — the C# compiler
/// hoists <c>r</c> into a generated locals class, so the predicate's right
/// side is a <see cref="System.Linq.Expressions.MemberExpression"/> rooted at
/// a <see cref="System.Linq.Expressions.ConstantExpression"/>, not a plain
/// <see cref="System.Linq.Expressions.ConstantExpression"/>. The pruner must
/// evaluate the closure to recover the key value; otherwise it falls through
/// to fan-out and every shard is hit.
/// </para>
/// </summary>
public class PredicatePruningIntegrationTests : IAsyncLifetime
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

    [Fact(DisplayName = "Closure-captured shard key: query enlists only the matching shard")]
    public async Task ClosureCapturedShardKey_EnlistsOnlyMatchingShard()
    {
        var (sp, scope, ctx, _, _, _) = await BuildThreeShardContextAsync();
        await using (sp)
        await using (scope)
        {
            await using var tx = await ctx.BeginCrossShardTransactionAsync();
            var crossShardTx = (CrossShardTransaction)tx;

            // Closure-captured shard-key value. With the pre-fix predicate
            // extractor, this compiled to (MemberExpression a.Region,
            // MemberExpression closure.target) and was ignored — so the
            // executor would fan out across all three shards.
            var target = "EU";

            var executor = scope.ServiceProvider.GetRequiredService<IShardedQueryExecutor>();
            await executor.ExecuteAsync(
                ctx.Set<PruneEntity>().Where(e => e.Region == target).AsQueryable());

            Assert.Equal("EU", Assert.Single(crossShardTx.EnlistedShards));

            await tx.RollbackAsync();
        }
    }

    [Fact(DisplayName = "Constant shard key: query still enlists only the matching shard (no regression)")]
    public async Task ConstantShardKey_StillEnlistsOnlyMatchingShard()
    {
        var (sp, scope, ctx, _, _, _) = await BuildThreeShardContextAsync();
        await using (sp)
        await using (scope)
        {
            await using var tx = await ctx.BeginCrossShardTransactionAsync();
            var crossShardTx = (CrossShardTransaction)tx;

            var executor = scope.ServiceProvider.GetRequiredService<IShardedQueryExecutor>();
            await executor.ExecuteAsync(
                ctx.Set<PruneEntity>().Where(e => e.Region == "US").AsQueryable());

            Assert.Equal("US", Assert.Single(crossShardTx.EnlistedShards));

            await tx.RollbackAsync();
        }
    }

    [Fact(DisplayName = "Method-call shard key: pruner does NOT invoke arbitrary code, falls back to fan-out")]
    public async Task MethodCallShardKey_FallsBackToFanOut()
    {
        // The pruner must not compile-and-invoke method calls in predicates.
        // If it did, a volatile / side-effecting method like NextRegion()
        // could yield a different value here than EF Core's parameter
        // extraction yields at query time — routing to the wrong shard and
        // missing rows. Fan-out is the safe fallback.
        var (sp, scope, ctx, _, _, _) = await BuildThreeShardContextAsync();
        await using (sp)
        await using (scope)
        {
            await using var tx = await ctx.BeginCrossShardTransactionAsync();
            var crossShardTx = (CrossShardTransaction)tx;

            var executor = scope.ServiceProvider.GetRequiredService<IShardedQueryExecutor>();
            await executor.ExecuteAsync(
                ctx.Set<PruneEntity>().Where(e => e.Region == StablePickRegion()).AsQueryable());

            Assert.Equal(3, crossShardTx.EnlistedShards.Count);

            await tx.RollbackAsync();
        }
    }

    [Fact(DisplayName = "Captured-property predicate (request.Region): query enlists only the matching shard")]
    public async Task CapturedPropertyShardKey_EnlistsOnlyMatchingShard()
    {
        // Equivalent to /within-tx-rollup in the Transactions sample:
        //   db.Set<Account>().Where(a => a.Region == request.Region)
        // The right side is a two-deep MemberExpression rooted at a
        // hoisted-locals ConstantExpression — same closure shape, one level
        // deeper than a captured local.
        var (sp, scope, ctx, _, _, _) = await BuildThreeShardContextAsync();
        await using (sp)
        await using (scope)
        {
            await using var tx = await ctx.BeginCrossShardTransactionAsync();
            var crossShardTx = (CrossShardTransaction)tx;

            var request = new PruneRequest("APAC");

            var executor = scope.ServiceProvider.GetRequiredService<IShardedQueryExecutor>();
            await executor.ExecuteAsync(
                ctx.Set<PruneEntity>().Where(e => e.Region == request.Region).AsQueryable());

            Assert.Equal("APAC", Assert.Single(crossShardTx.EnlistedShards));

            await tx.RollbackAsync();
        }
    }

    // Used only by MethodCallShardKey_FallsBackToFanOut. Constant return —
    // the test asserts pruner behaviour, not the value.
    private static string StablePickRegion() => "EU";

    private async Task<(ServiceProvider sp, AsyncServiceScope scope, PruneDbContext ctx, SqliteConnection eu, SqliteConnection us, SqliteConnection apac)>
        BuildThreeShardContextAsync()
    {
        var dbId = Guid.NewGuid().ToString("N");
        var euConn = $"Data Source=eu_prune_{dbId};Mode=Memory;Cache=Shared";
        var usConn = $"Data Source=us_prune_{dbId};Mode=Memory;Cache=Shared";
        var apacConn = $"Data Source=apac_prune_{dbId};Mode=Memory;Cache=Shared";

        var euAnchor = new SqliteConnection(euConn);
        var usAnchor = new SqliteConnection(usConn);
        var apacAnchor = new SqliteConnection(apacConn);
        await euAnchor.OpenAsync();
        await usAnchor.OpenAsync();
        await apacAnchor.OpenAsync();
        _connections.Add(euAnchor);
        _connections.Add(usAnchor);
        _connections.Add(apacAnchor);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDtdeDbContext<PruneDbContext>(
            (db, conn) => db.UseSqlite(conn ?? "Data Source=:memory:"),
            dtde => dtde
                .AddShard("EU", euConn)
                .AddShard("US", usConn)
                .AddShard("APAC", apacConn));

        var sp = services.BuildServiceProvider();
        var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<PruneDbContext>();
        await ctx.EnsureAllShardsCreatedAsync();

        return (sp, scope, ctx, euAnchor, usAnchor, apacAnchor);
    }
}

#pragma warning disable CA1062 // Test fixture: modelBuilder null check is unnecessary noise.

public sealed record PruneRequest(string Region);

public class PruneEntity
{
    public int Id { get; set; }
    public string Region { get; set; } = string.Empty;
}

public class PruneDbContext : DtdeDbContext
{
    public DbSet<PruneEntity> PruneEntities => Set<PruneEntity>();

    public PruneDbContext(DbContextOptions<PruneDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PruneEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Region).HasMaxLength(10).IsRequired();
            e.ShardBy(x => x.Region);
        });
    }
}

#pragma warning restore CA1062
