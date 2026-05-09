# Migration guide

How to migrate an existing Entity Framework Core application to DTDE.

## Pre-flight check

- [ ] You're on .NET 8 / 9 / 10.
- [ ] Your `DbContext` inherits from `DbContext`.
- [ ] You have a backup of production data and the ability to roll back.
- [ ] You've read [Getting started](getting-started.md).

## Step 1 — Install

```bash
dotnet add package Dtde.EntityFramework
```

This transitively pulls in `Dtde.Core` and `Dtde.Abstractions`.

## Step 2 — Inherit from `DtdeDbContext`

```diff
- public class AppDbContext : DbContext
+ public class AppDbContext : DtdeDbContext
  {
      public AppDbContext(DbContextOptions<AppDbContext> options)
          : base(options) { }

      public DbSet<Customer> Customers => Set<Customer>();
  }
```

`DtdeDbContext` is a thin subclass of EF Core's `DbContext` — your
existing model, migrations, and query patterns keep working unchanged.

## Step 3 — Switch DI registration

```diff
- services.AddDbContext<AppDbContext>(options =>
-     options.UseSqlServer(configuration.GetConnectionString("Default")));
+ services.AddDtdeDbContext<AppDbContext>(
+     (db, conn) => db.UseSqlServer(conn ?? configuration.GetConnectionString("Default")),
+     dtde => dtde.AddShards("EU", "US", "APAC"));
```

The provider callback is now `(db, conn) => ...`. DTDE invokes it with
`conn = null` for the parent context (fall back to a default) and with
the shard's connection for each per-shard context. Your existing
`UseSqlServer(...)` / `UseSqlite(...)` / etc. call goes inside the
callback.

## Step 4 — Annotate sharded entities

Inside `OnModelCreating`:

```diff
  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
      base.OnModelCreating(modelBuilder);

      modelBuilder.Entity<Customer>(entity =>
      {
          entity.HasKey(c => c.Id);
          entity.Property(c => c.Region).HasMaxLength(10).IsRequired();
+         entity.ShardBy(c => c.Region);
      });
  }
```

`ShardBy` returns a fluent `ShardingBuilder<T>` for chaining
(`.WithStorageMode(...)`, `.WithTablePattern(...)`, `.UseShardGroup(...)`,
`.WithoutMigrations()`).

## Step 5 — Provision shard tables

For samples, dev environments, and integration tests:

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.EnsureAllShardsCreatedAsync();
}
```

For production, use EF Core migrations against each shard's connection
manually. (Per-shard migrations as a first-class feature isn't yet
shipped.)

## Step 6 — Verify

Run your existing query workload. Standard LINQ continues to work; DTDE
prunes `Where(... shard-key ...)` queries to the matching shard, fans
the rest out across all shards, and merges results.

## Migrating existing data

Two paths:

### A. Read-and-rewrite (small datasets)

```csharp
// Read everything from the legacy table.
var existing = await legacyDb.Customers.ToListAsync();

// Bulk-insert into DTDE — entities route to their target shard automatically.
await dtdeDb.BulkInsertAsync(existing);
```

### B. SQL-level rewrite (large datasets)

For tables too big to fit in memory, write a one-shot `INSERT ... SELECT`
migration script per target shard:

```sql
-- Run once per region.
INSERT INTO Customers_EU (Id, Name, Region, ...)
SELECT Id, Name, Region, ... FROM Customers_legacy
WHERE Region = 'EU';
```

Then drop the legacy table.

## Adding shard groups for mixed strategies

If your application has two entities with **different** shard
topologies — say users hashed into 8 buckets *and* orders bucketed by
year — bind each to a named group:

```csharp
// Program.cs
dtde => dtde
    .AddShardGroup("hash8", g => g.AddShards("0","1","2","3","4","5","6","7"))
    .AddShardGroup("years", g => g.AddShards("2023","2024","2025"));

// OnModelCreating
modelBuilder.Entity<UserProfile>().ShardByHash(u => u.UserId, 8).UseShardGroup("hash8");
modelBuilder.Entity<Order>().ShardByDate(o => o.OrderDate).UseShardGroup("years");
```

If you don't call `AddShardGroup` and don't call `UseShardGroup`,
everything goes into the implicit default group — the simple case stays
configuration-free.

## Adding cross-shard transactions

`SaveChangesAsync` auto-promotes to a cross-shard transaction
automatically when changes span multiple shards — nothing to migrate.
For explicit control:

```csharp
await using var tx = await db.BeginCrossShardTransactionAsync();

// ... writes ...

await tx.CommitAsync();
```

See [cross-shard transactions](cross-shard-transactions.md).

## Adding crash recovery

Default in-memory log doesn't survive process restarts. For durable
recovery:

```csharp
services.AddSingleton<ITransactionLog>(_ =>
    new FileBasedTransactionLog("/var/dtde/tx-log.jsonl"));

services.AddDtdeDbContext<AppDbContext>(...);
```

On startup, before accepting traffic:

```csharp
var coordinator = scope.ServiceProvider.GetRequiredService<ICrossShardTransactionCoordinator>();
await coordinator.RecoverAsync();
```

See [transaction log and recovery](transaction-log-and-recovery.md).

## Rollback

DTDE only intercepts at the DbContext layer. To roll back:

1. Revert the `DtdeDbContext` base back to `DbContext`.
2. Revert the `AddDtdeDbContext` call back to `AddDbContext`.
3. Remove the `ShardBy*` annotations from `OnModelCreating`.
4. Optionally consolidate the per-shard tables back into a single table.

The DTDE NuGet packages can stay installed without effect — nothing in
DTDE runs unless `AddDtdeDbContext` is called.

## What didn't change

- Your entities. Add a `Region` (or whatever shard-key) property if
  you don't already have one.
- Your queries. Standard LINQ.
- Your transactions. EF Core's `BeginTransactionAsync` still works
  for single-shard scenarios.
- Your tests. DTDE works against any EF Core provider — SQLite,
  SQL Server, PostgreSQL, in-memory.

## See also

- [Getting started](getting-started.md) — 5-minute walk-through.
- [Sharding guide](sharding-guide.md) — strategies, modes, groups.
- [Cross-shard transactions](cross-shard-transactions.md) — atomicity.
- [Bulk operations](bulk-operations.md) — for the data-migration pass.
