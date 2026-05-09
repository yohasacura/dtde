using System.Data.Common;

using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dtde.Integration.Tests.Bulk;

/// <summary>
/// End-to-end tests for the bulk-operation extensions.
/// </summary>
public class BulkOperationsIntegrationTests : IAsyncLifetime
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

    [Fact(DisplayName = "BulkInsertAsync routes each entity to its target shard")]
    public async Task BulkInsertAsync_RoutesEntitiesByShardKey()
    {
        var (sp, scope, ctx, euAnchor, usAnchor, apacAnchor) = await BuildThreeShardContextAsync();
        await using (sp)
        await using (scope)
        {
            var entities = new List<BulkCustomer>
            {
                new() { Id = 1, Name = "Alice", Region = "EU" },
                new() { Id = 2, Name = "Bob", Region = "US" },
                new() { Id = 3, Name = "Cleo", Region = "APAC" },
                new() { Id = 4, Name = "Dan",  Region = "EU" },
                new() { Id = 5, Name = "Eve",  Region = "US" },
            };

            var inserted = await ctx.BulkInsertAsync(entities);
            Assert.Equal(5, inserted);

            Assert.Equal(2, await CountRowsAsync(euAnchor, "BulkCustomers"));
            Assert.Equal(2, await CountRowsAsync(usAnchor, "BulkCustomers"));
            Assert.Equal(1, await CountRowsAsync(apacAnchor, "BulkCustomers"));
        }
    }

    [Fact(DisplayName = "BulkInsertAsync single-shard fast path: all rows route to one shard, no 2PC overhead")]
    public async Task BulkInsertAsync_SingleShardFastPath()
    {
        var (sp, scope, ctx, euAnchor, usAnchor, apacAnchor) = await BuildThreeShardContextAsync();
        await using (sp)
        await using (scope)
        {
            // All five entities have Region = "EU" — only the EU shard is touched.
            var entities = Enumerable.Range(1, 5)
                .Select(i => new BulkCustomer { Id = i, Name = $"E{i}", Region = "EU" })
                .ToList();

            var inserted = await ctx.BulkInsertAsync(entities);
            Assert.Equal(5, inserted);

            Assert.Equal(5, await CountRowsAsync(euAnchor, "BulkCustomers"));
            Assert.Equal(0, await CountRowsAsync(usAnchor, "BulkCustomers"));
            Assert.Equal(0, await CountRowsAsync(apacAnchor, "BulkCustomers"));
        }
    }

    [Fact(DisplayName = "BulkInsertAsync handles empty input as a no-op")]
    public async Task BulkInsertAsync_EmptyInput_ReturnsZero()
    {
        var (sp, scope, ctx, _, _, _) = await BuildThreeShardContextAsync();
        await using (sp)
        await using (scope)
        {
            var inserted = await ctx.BulkInsertAsync(Array.Empty<BulkCustomer>());
            Assert.Equal(0, inserted);
        }
    }

    [Fact(DisplayName = "BulkDeleteAsync deletes matching rows across every shard in the entity's group")]
    public async Task BulkDeleteAsync_FansOutAcrossGroup()
    {
        var (sp, scope, ctx, euAnchor, usAnchor, apacAnchor) = await BuildThreeShardContextAsync();
        await using (sp)
        await using (scope)
        {
            // Seed three shards with mixed data.
            await ctx.BulkInsertAsync(new[]
            {
                new BulkCustomer { Id = 1, Name = "active",   Region = "EU"   },
                new BulkCustomer { Id = 2, Name = "inactive", Region = "EU"   },
                new BulkCustomer { Id = 3, Name = "active",   Region = "US"   },
                new BulkCustomer { Id = 4, Name = "inactive", Region = "US"   },
                new BulkCustomer { Id = 5, Name = "inactive", Region = "APAC" },
            });

            // Bulk-delete every "inactive" row in every shard.
            var deleted = await ctx.BulkDeleteAsync<BulkCustomer>(c => c.Name == "inactive");

            Assert.Equal(3, deleted);

            // The "active" rows survive.
            Assert.Equal(1, await CountRowsAsync(euAnchor, "BulkCustomers"));
            Assert.Equal(1, await CountRowsAsync(usAnchor, "BulkCustomers"));
            Assert.Equal(0, await CountRowsAsync(apacAnchor, "BulkCustomers"));
        }
    }

    private async Task<(ServiceProvider sp, AsyncServiceScope scope, BulkOpsDbContext ctx, SqliteConnection euAnchor, SqliteConnection usAnchor, SqliteConnection apacAnchor)>
        BuildThreeShardContextAsync()
    {
        var dbId = Guid.NewGuid().ToString("N");
        var euConn = $"Data Source=eu_bulk_{dbId};Mode=Memory;Cache=Shared";
        var usConn = $"Data Source=us_bulk_{dbId};Mode=Memory;Cache=Shared";
        var apacConn = $"Data Source=apac_bulk_{dbId};Mode=Memory;Cache=Shared";

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
        services.AddDtdeDbContext<BulkOpsDbContext>(
            (db, conn) => db.UseSqlite(conn ?? "Data Source=:memory:"),
            dtde => dtde
                .AddShard("EU", euConn)
                .AddShard("US", usConn)
                .AddShard("APAC", apacConn));

        var sp = services.BuildServiceProvider();
        var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BulkOpsDbContext>();
        await ctx.EnsureAllShardsCreatedAsync();

        return (sp, scope, ctx, euAnchor, usAnchor, apacAnchor);
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

public class BulkCustomer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
}

public class BulkOpsDbContext : DtdeDbContext
{
    public DbSet<BulkCustomer> BulkCustomers => Set<BulkCustomer>();

    public BulkOpsDbContext(DbContextOptions<BulkOpsDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<BulkCustomer>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Region).HasMaxLength(10).IsRequired();
            e.Property(c => c.Name).HasMaxLength(100).IsRequired();
            e.ShardBy(c => c.Region);
        });
    }
}

#pragma warning restore CA1062
