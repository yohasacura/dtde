using System.Data.Common;

using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dtde.Integration.Tests.Sharding;

/// <summary>
/// End-to-end tests that verify DTDE actually routes data per shard:
/// <list type="bullet">
///   <item><description>Table-mode: a single SQLite file gets per-shard tables (<c>Customers_EU</c>, <c>Customers_US</c>) and rows land in the right one.</description></item>
///   <item><description>Database-mode: separate SQLite files (one per shard) each get their own <c>Customers</c> table; rows are written to the correct DB.</description></item>
/// </list>
/// </summary>
public class RealShardingIntegrationTests : IAsyncLifetime
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

    [Fact(DisplayName = "Table-mode: per-shard tables exist after EnsureAllShardsCreatedAsync")]
    public async Task TableMode_PerShardTablesExist()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        _connections.Add(conn);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDtdeDbContext<RegionDbContext>(
            (db, _) => db.UseSqlite(conn),
            dtde => dtde.AddShards("EU", "US", "APAC"));

        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RegionDbContext>();

        await ctx.EnsureAllShardsCreatedAsync();

        var tables = await ListTablesAsync(conn);

        Assert.Contains("Customers_EU", tables);
        Assert.Contains("Customers_US", tables);
        Assert.Contains("Customers_APAC", tables);
    }

    [Fact(DisplayName = "Table-mode: writes land in the per-shard table matching the shard key")]
    public async Task TableMode_WritesLandInTheRightTable()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        _connections.Add(conn);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDtdeDbContext<RegionDbContext>(
            (db, _) => db.UseSqlite(conn),
            dtde => dtde.AddShards("EU", "US"));

        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RegionDbContext>();
        await ctx.EnsureAllShardsCreatedAsync();

        // Write through a per-shard context
        var contextFactory = scope.ServiceProvider.GetRequiredService<Dtde.EntityFramework.Query.IShardContextFactory>();
        await using (var euCtx = await contextFactory.CreateContextAsync("EU"))
        {
            euCtx.Set<Customer>().Add(new Customer { Id = 1, Name = "EU Customer", Region = "EU" });
            await euCtx.SaveChangesAsync();
        }
        await using (var usCtx = await contextFactory.CreateContextAsync("US"))
        {
            usCtx.Set<Customer>().Add(new Customer { Id = 2, Name = "US Customer", Region = "US" });
            await usCtx.SaveChangesAsync();
        }

        var euCount = await CountRowsAsync(conn, "Customers_EU");
        var usCount = await CountRowsAsync(conn, "Customers_US");
        Assert.Equal(1, euCount);
        Assert.Equal(1, usCount);
    }

    [Fact(DisplayName = "Database-mode: each shard's connection string gets its own SQLite database")]
    public async Task DatabaseMode_EachShardGetsItsOwnDatabase()
    {
        // Use named shared-cache in-memory databases so the test's anchor
        // connection and the per-shard factory's connection see the same
        // SQLite database. The unique id keeps this test isolated from any
        // previous run in the same process.
        var dbId = Guid.NewGuid().ToString("N");
        var euConnString = $"Data Source=eu_{dbId};Mode=Memory;Cache=Shared";
        var usConnString = $"Data Source=us_{dbId};Mode=Memory;Cache=Shared";

        var euAnchor = new SqliteConnection(euConnString);
        var usAnchor = new SqliteConnection(usConnString);
        await euAnchor.OpenAsync();
        await usAnchor.OpenAsync();
        _connections.Add(euAnchor);
        _connections.Add(usAnchor);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDtdeDbContext<RegionDbContext>(
            (db, conn) => db.UseSqlite(conn ?? "Data Source=:memory:"),
            dtde => dtde
                .AddShard("EU", euConnString)
                .AddShard("US", usConnString));

        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RegionDbContext>();

        await ctx.EnsureAllShardsCreatedAsync();

        // Diagnostic: confirm Customers exists on each shard's DB before we
        // try to write to it. (Helps narrow down failures: provisioning
        // problem vs routing problem.)
        var euTables = await ListTablesAsync(euAnchor);
        var usTables = await ListTablesAsync(usAnchor);
        Assert.Contains("Customers", euTables);
        Assert.Contains("Customers", usTables);

        var contextFactory = scope.ServiceProvider.GetRequiredService<Dtde.EntityFramework.Query.IShardContextFactory>();

        await using (var euCtx = await contextFactory.CreateContextAsync("EU"))
        {
            euCtx.Set<Customer>().Add(new Customer { Id = 1, Name = "Alice", Region = "EU" });
            await euCtx.SaveChangesAsync();
        }
        await using (var usCtx = await contextFactory.CreateContextAsync("US"))
        {
            usCtx.Set<Customer>().Add(new Customer { Id = 2, Name = "Bob", Region = "US" });
            await usCtx.SaveChangesAsync();
        }

        // Each shard's database has its own Customers table — table-name
        // rewriting is intentionally skipped for database-mode shards.
        var euRows = await CountRowsAsync(euAnchor, "Customers");
        var usRows = await CountRowsAsync(usAnchor, "Customers");
        Assert.Equal(1, euRows);
        Assert.Equal(1, usRows);
    }

    private static async Task<List<string>> ListTablesAsync(DbConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        var names = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }

#pragma warning disable CA2100 // tableName comes from test fixture identifiers, not user input
    private static async Task<long> CountRowsAsync(DbConnection connection, string tableName)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{tableName}\"";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }
#pragma warning restore CA2100
}

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
}

public class RegionDbContext : DtdeDbContext
{
    public DbSet<Customer> Customers => Set<Customer>();

    public RegionDbContext(DbContextOptions<RegionDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Region).HasMaxLength(10).IsRequired();
            entity.ShardBy(c => c.Region);
        });
    }
}
