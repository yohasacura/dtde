using System.Data.Common;

using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
using Dtde.EntityFramework.Update;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dtde.Integration.Tests.Bulk;

/// <summary>
/// End-to-end tests for the bulk-operation depth features:
/// <list type="bullet">
///   <item><description>Streaming fan-out via <c>ExecuteStreamingAsync</c>.</description></item>
///   <item><description>Pluggable <c>IBulkInsertProvider</c> precedence (custom > default).</description></item>
///   <item><description><c>BulkUpdateAsync</c> across shards.</description></item>
///   <item><description>Bulk operations participating in an ambient cross-shard transaction.</description></item>
/// </list>
/// </summary>
public class BulkDepthIntegrationTests : IAsyncLifetime
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

    [Fact(DisplayName = "ExecuteStreamingAsync yields entities from all shards without buffering everything in memory")]
    public async Task ExecuteStreamingAsync_StreamsAcrossShards()
    {
        var (sp, scope, ctx, _, _, _) = await BuildThreeShardContextAsync();
        await using (sp)
        await using (scope)
        {
            var seed = new List<DepthCustomer>();
            for (var i = 0; i < 30; i++)
            {
                var region = (i % 3) switch { 0 => "EU", 1 => "US", _ => "APAC" };
                seed.Add(new DepthCustomer { Id = i + 1, Name = $"c{i}", Region = region });
            }
            await ctx.BulkInsertAsync(seed);

            var executor = scope.ServiceProvider
                .GetRequiredService<Dtde.EntityFramework.Query.IShardedQueryExecutor>();

            var streamed = new List<DepthCustomer>();
            await foreach (var customer in executor.ExecuteStreamingAsync(
                ctx.Set<DepthCustomer>().AsQueryable()))
            {
                streamed.Add(customer);
            }

            Assert.Equal(30, streamed.Count);
            Assert.Equal(seed.Select(s => s.Id).OrderBy(id => id),
                         streamed.Select(s => s.Id).OrderBy(id => id));
        }
    }

    [Fact(DisplayName = "Custom IBulkInsertProvider wins over the default provider")]
    public async Task CustomBulkInsertProvider_TakesPrecedenceOverDefault()
    {
        var dbId = Guid.NewGuid().ToString("N");
        var euConn = $"Data Source=eu_provider_{dbId};Mode=Memory;Cache=Shared";
        var euAnchor = new SqliteConnection(euConn);
        await euAnchor.OpenAsync();
        _connections.Add(euAnchor);

        var customProvider = new RecordingBulkInsertProvider();

        var services = new ServiceCollection();
        services.AddLogging();
        // Register the custom provider BEFORE AddDtdeDbContext so it's
        // ahead of the default in the IEnumerable<IBulkInsertProvider>.
        services.AddSingleton<IBulkInsertProvider>(customProvider);
        services.AddDtdeDbContext<DepthBulkContext>(
            (db, conn) => db.UseSqlite(conn ?? "Data Source=:memory:"),
            dtde => dtde.AddShard("EU", euConn));

        var sp = services.BuildServiceProvider();
        var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<DepthBulkContext>();
        await ctx.EnsureAllShardsCreatedAsync();

        await using (sp)
        await using (scope)
        {
            await ctx.BulkInsertAsync(new[]
            {
                new DepthCustomer { Id = 1, Region = "EU", Name = "x" },
                new DepthCustomer { Id = 2, Region = "EU", Name = "y" },
            });

            // The custom provider was invoked, not the default.
            Assert.Equal(1, customProvider.InvocationCount);
            Assert.Equal(2, customProvider.LastBatchSize);
        }
    }

    [Fact(DisplayName = "BulkUpdateAsync applies the update across every shard in the entity's group")]
    public async Task BulkUpdateAsync_FansOutAcrossGroup()
    {
        var (sp, scope, ctx, euAnchor, usAnchor, apacAnchor) = await BuildThreeShardContextAsync();
        await using (sp)
        await using (scope)
        {
            await ctx.BulkInsertAsync(new[]
            {
                new DepthCustomer { Id = 1, Region = "EU", Name = "old" },
                new DepthCustomer { Id = 2, Region = "EU", Name = "old" },
                new DepthCustomer { Id = 3, Region = "US", Name = "old" },
                new DepthCustomer { Id = 4, Region = "APAC", Name = "old" },
            });

#if NET10_0_OR_GREATER
            var updated = await ctx.BulkUpdateAsync<DepthCustomer>(
                c => c.Name == "old",
                setters => setters.SetProperty(c => c.Name, "new"));
#else
            var updated = await ctx.BulkUpdateAsync<DepthCustomer>(
                c => c.Name == "old",
                p => p.SetProperty(c => c.Name, "new"));
#endif

            Assert.Equal(4, updated);

            // Verify by querying — every row's Name is now "new".
            var executor = scope.ServiceProvider
                .GetRequiredService<Dtde.EntityFramework.Query.IShardedQueryExecutor>();
            var rows = await executor.ExecuteAsync(
                ctx.Set<DepthCustomer>().AsQueryable());

            Assert.Equal(4, rows.Count);
            Assert.All(rows, r => Assert.Equal("new", r.Name));
        }
    }

    [Fact(DisplayName = "BulkInsertAsync inside an ambient cross-shard transaction commits with the transaction")]
    public async Task BulkInsertAsync_InsideAmbientTransaction_CommitsWithIt()
    {
        var (sp, scope, ctx, euAnchor, usAnchor, _) = await BuildThreeShardContextAsync();
        await using (sp)
        await using (scope)
        {
            await using (var tx = await ctx.BeginCrossShardTransactionAsync())
            {
                // Bulk insert inside the ambient transaction — every shard
                // is auto-enlisted as a participant.
                await ctx.BulkInsertAsync(new[]
                {
                    new DepthCustomer { Id = 1, Region = "EU", Name = "first" },
                    new DepthCustomer { Id = 2, Region = "US", Name = "first" },
                });

                // Simulate failure: throw out of the transaction scope so it
                // rolls back instead of committing.
                await tx.RollbackAsync();
            }

            Assert.Equal(0, await CountRowsAsync(euAnchor, "DepthCustomers"));
            Assert.Equal(0, await CountRowsAsync(usAnchor, "DepthCustomers"));

            // Now do it again with commit — verify the inserts persist.
            await using (var tx = await ctx.BeginCrossShardTransactionAsync())
            {
                await ctx.BulkInsertAsync(new[]
                {
                    new DepthCustomer { Id = 10, Region = "EU", Name = "second" },
                    new DepthCustomer { Id = 11, Region = "US", Name = "second" },
                });
                await tx.CommitAsync();
            }

            Assert.Equal(1, await CountRowsAsync(euAnchor, "DepthCustomers"));
            Assert.Equal(1, await CountRowsAsync(usAnchor, "DepthCustomers"));
        }
    }

    private async Task<(ServiceProvider sp, AsyncServiceScope scope, DepthBulkContext ctx, SqliteConnection euAnchor, SqliteConnection usAnchor, SqliteConnection apacAnchor)>
        BuildThreeShardContextAsync()
    {
        var dbId = Guid.NewGuid().ToString("N");
        var euConn = $"Data Source=eu_depth_{dbId};Mode=Memory;Cache=Shared";
        var usConn = $"Data Source=us_depth_{dbId};Mode=Memory;Cache=Shared";
        var apacConn = $"Data Source=apac_depth_{dbId};Mode=Memory;Cache=Shared";

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
        services.AddDtdeDbContext<DepthBulkContext>(
            (db, conn) => db.UseSqlite(conn ?? "Data Source=:memory:"),
            dtde => dtde
                .AddShard("EU", euConn)
                .AddShard("US", usConn)
                .AddShard("APAC", apacConn));

        var sp = services.BuildServiceProvider();
        var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<DepthBulkContext>();
        await ctx.EnsureAllShardsCreatedAsync();

        return (sp, scope, ctx, euAnchor, usAnchor, apacAnchor);
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

/// <summary>
/// Test-only provider that records calls and delegates to the standard
/// EF path. Demonstrates the plug-in shape for SqlBulkCopy / PG COPY.
/// </summary>
internal sealed class RecordingBulkInsertProvider : IBulkInsertProvider
{
    public int InvocationCount { get; private set; }
    public int LastBatchSize { get; private set; }

    public bool CanHandle(DbContext context) => true;

    public async Task<int> BulkInsertAsync<TEntity>(
        DbContext context,
        IReadOnlyCollection<TEntity> entities,
        CancellationToken cancellationToken = default) where TEntity : class
    {
        InvocationCount++;
        LastBatchSize = entities.Count;
        await context.Set<TEntity>().AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entities.Count;
    }
}

#pragma warning disable CA1062

public class DepthCustomer
{
    public int Id { get; set; }
    public string Region { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class DepthBulkContext : DtdeDbContext
{
    public DbSet<DepthCustomer> DepthCustomers => Set<DepthCustomer>();

    public DepthBulkContext(DbContextOptions<DepthBulkContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DepthCustomer>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Region).HasMaxLength(10).IsRequired();
            e.Property(c => c.Name).HasMaxLength(100);
            e.ShardBy(c => c.Region);
        });
    }
}

#pragma warning restore CA1062
