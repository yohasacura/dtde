using System.Data.Common;

using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dtde.Integration.Tests.Sharding;

/// <summary>
/// End-to-end tests for per-entity shard groups: two entities with different
/// shard topologies inside the same DbContext route, provision, and query
/// independently.
/// </summary>
public class ShardGroupsIntegrationTests : IAsyncLifetime
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

    [Fact(DisplayName = "Different entities bind to different groups; provisioning is per-group, per-entity")]
    public async Task DifferentEntities_BindToDifferentGroups_AreProvisionedSeparately()
    {
        // Two databases — one per group — keeps the bookkeeping simple and
        // proves provisioning skips out-of-group entities.
        var dbId = Guid.NewGuid().ToString("N");
        var hashConn = $"Data Source=hash_{dbId};Mode=Memory;Cache=Shared";
        var yearsConn = $"Data Source=years_{dbId};Mode=Memory;Cache=Shared";

        var hashAnchor = new SqliteConnection(hashConn);
        var yearsAnchor = new SqliteConnection(yearsConn);
        await hashAnchor.OpenAsync();
        await yearsAnchor.OpenAsync();
        _connections.Add(hashAnchor);
        _connections.Add(yearsAnchor);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDtdeDbContext<MultiGroupDbContext>(
            (db, conn) => db.UseSqlite(conn ?? "Data Source=:memory:"),
            dtde => dtde
                .AddShardGroup("hash3", g => g
                    .AddTableShardInDatabase("0", hashConn)
                    .AddTableShardInDatabase("1", hashConn)
                    .AddTableShardInDatabase("2", hashConn))
                .AddShardGroup("years", g => g
                    .AddTableShardInDatabase("2023", yearsConn)
                    .AddTableShardInDatabase("2024", yearsConn)
                    .AddTableShardInDatabase("2025", yearsConn)));

        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MultiGroupDbContext>();

        await ctx.EnsureAllShardsCreatedAsync();

        var hashTables = await ListTablesAsync(hashAnchor);
        var yearsTables = await ListTablesAsync(yearsAnchor);

        // hash group's database has UserProfile_0..2, but no Orders_2023 etc.
        Assert.Contains("UserProfiles_0", hashTables);
        Assert.Contains("UserProfiles_1", hashTables);
        Assert.Contains("UserProfiles_2", hashTables);
        Assert.DoesNotContain(hashTables, t => t.StartsWith("Orders_", StringComparison.Ordinal));

        // years group's database has Orders_2023..2025, but no UserProfiles_*.
        Assert.Contains("Orders_2023", yearsTables);
        Assert.Contains("Orders_2024", yearsTables);
        Assert.Contains("Orders_2025", yearsTables);
        Assert.DoesNotContain(yearsTables, t => t.StartsWith("UserProfiles_", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Same local shard id in two groups maps to two physical shards")]
    public async Task SameLocalShardId_InDifferentGroups_StaysIsolated()
    {
        // Both groups use "0" as a shard id; they must remain physically
        // distinct and route entities only to their own shard.
        var dbId = Guid.NewGuid().ToString("N");
        var aConn = $"Data Source=group_a_{dbId};Mode=Memory;Cache=Shared";
        var bConn = $"Data Source=group_b_{dbId};Mode=Memory;Cache=Shared";

        var aAnchor = new SqliteConnection(aConn);
        var bAnchor = new SqliteConnection(bConn);
        await aAnchor.OpenAsync();
        await bAnchor.OpenAsync();
        _connections.Add(aAnchor);
        _connections.Add(bAnchor);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDtdeDbContext<MultiGroupDbContext>(
            (db, conn) => db.UseSqlite(conn ?? "Data Source=:memory:"),
            dtde => dtde
                .AddShardGroup("hash3", g => g
                    .AddTableShardInDatabase("0", aConn)
                    .AddTableShardInDatabase("1", aConn)
                    .AddTableShardInDatabase("2", aConn))
                .AddShardGroup("years", g => g
                    .AddTableShardInDatabase("2024", bConn)));

        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MultiGroupDbContext>();
        await ctx.EnsureAllShardsCreatedAsync();

        // Group A has UserProfiles_0; Group B doesn't (its only shard is 2024 → Orders_2024).
        var aTables = await ListTablesAsync(aAnchor);
        var bTables = await ListTablesAsync(bAnchor);

        Assert.Contains("UserProfiles_0", aTables);
        Assert.DoesNotContain("UserProfiles_0", bTables);
        Assert.DoesNotContain("Orders_0", aTables);
        Assert.Contains("Orders_2024", bTables);
    }

    [Fact(DisplayName = "Default group still works when no AddShardGroup is called")]
    public async Task DefaultGroup_ImplicitForEntitiesAndShards_StillWorks()
    {
        // Single SQLite file, three regions, the simple "everything in one
        // shard topology" case. No groups declared — the default group covers
        // it.
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        _connections.Add(conn);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDtdeDbContext<RegionsOnlyDbContext>(
            (db, _) => db.UseSqlite(conn),
            dtde => dtde.AddShards("EU", "US", "APAC"));

        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RegionsOnlyDbContext>();

        await ctx.EnsureAllShardsCreatedAsync();

        var tables = await ListTablesAsync(conn);
        Assert.Contains("RegionalCustomers_EU", tables);
        Assert.Contains("RegionalCustomers_US", tables);
        Assert.Contains("RegionalCustomers_APAC", tables);
    }

    [Fact(DisplayName = "Entity declares unknown shard group → throws at first DbContext use")]
    public async Task EntityWithUnknownShardGroup_ThrowsAtFirstDbContextUse()
    {
        // The entity binds to "missing-group", but the application never
        // declared it. The model customizer's startup validation surfaces the
        // misconfiguration the first time anything touches the model — clear
        // error message naming both the entity and the missing group.
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        _connections.Add(conn);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDtdeDbContext<UnknownGroupDbContext>(
            (db, _) => db.UseSqlite(conn),
            dtde => dtde.AddShards("EU"));

        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<UnknownGroupDbContext>();

        var ex = Assert.Throws<InvalidOperationException>(() => _ = ctx.Model);

        Assert.Contains("missing-group", ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(UserProfile), ex.Message, StringComparison.Ordinal);
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
}

#pragma warning disable CA1062 // Test fixtures: modelBuilder null check is unnecessary noise here.

public class UserProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ShardKey { get; set; } = string.Empty;
}

public class Order
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Year { get; set; } = string.Empty;
}

public class MultiGroupDbContext : DtdeDbContext
{
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<Order> Orders => Set<Order>();

    public MultiGroupDbContext(DbContextOptions<MultiGroupDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.ShardKey).HasMaxLength(10).IsRequired();
            entity.ShardBy(u => u.ShardKey).UseShardGroup("hash3");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(o => o.Id);
            entity.Property(o => o.Year).HasMaxLength(8).IsRequired();
            entity.ShardBy(o => o.Year).UseShardGroup("years");
        });
    }
}

public class RegionalCustomer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
}

public class RegionsOnlyDbContext : DtdeDbContext
{
    public DbSet<RegionalCustomer> RegionalCustomers => Set<RegionalCustomer>();

    public RegionsOnlyDbContext(DbContextOptions<RegionsOnlyDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<RegionalCustomer>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Region).HasMaxLength(10).IsRequired();
            entity.ShardBy(c => c.Region); // No UseShardGroup → default group.
        });
    }
}

public class UnknownGroupDbContext : DtdeDbContext
{
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public UnknownGroupDbContext(DbContextOptions<UnknownGroupDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.ShardKey).HasMaxLength(10).IsRequired();
            entity.ShardBy(u => u.ShardKey).UseShardGroup("missing-group");
        });
    }
}

#pragma warning restore CA1062
