# Sharding guide

DTDE distributes a sharded entity's rows across multiple **shards**.
This guide is the canonical reference for the routing rules — when to
pick which strategy, how storage modes work, when you need shard
groups, and how queries fan out.

For a 5-minute hands-on intro, see [Getting started](getting-started.md).

## How DTDE routes

Two layers, one contract:

1. **Per entity** (in `OnModelCreating`) you declare *how* a row maps
   to a shard key — `ShardBy`, `ShardByDate`, `ShardByHash`, or the
   manual catch-all.
2. **In `Program.cs`** you declare *the shards themselves* — their
   ids, where they live (table-mode, database-mode, mixed-mode), and
   which **shard group** they belong to.

The contract that ties them together: the shard's id (the string you
pass to `AddShard("EU")` or `AddShards("0", "1", ...)`) must equal the
value the entity's shard-key property carries at runtime, modulo the
strategy's transform (e.g. hash-modulo-N, year extraction). If a row's
shard key doesn't match any registered shard, DTDE throws at write
time.

## Strategies

### `ShardBy` — property value

Use when the shard key is a categorical property: region, tenant id,
country code, etc.

```csharp
modelBuilder.Entity<Customer>().ShardBy(c => c.Region);

dtde => dtde.AddShards("EU", "US", "APAC");
```

The shard chosen for an insert is the one whose id equals
`customer.Region.ToString()` (case-insensitive). Queries with a
`Where(c => c.Region == "EU")` predicate prune to one shard; queries
without one fan out across all registered shards.

Best for: GDPR-style regional residency, multi-tenant SaaS, anywhere
the shard key is human-meaningful.

### `ShardByHash` — even distribution

Use when the shard key has no semantic meaning — you just want load
spread evenly.

```csharp
modelBuilder.Entity<UserProfile>().ShardByHash(u => u.UserId, shardCount: 8);

dtde => dtde.AddShards("0", "1", "2", "3", "4", "5", "6", "7");
```

DTDE computes the user-id hash modulo `shardCount` to pick a shard.
Queries with `Where(u => u.UserId == X)` prune to one shard (the hash
locates it); queries without fan out across all `shardCount` shards.

Best for: anything keyed by a UUID / sequential ID where you want
predictable, hot-spot-free distribution.

### `ShardByDate` — time bucketing

Use when data has a natural time component and you want hot/warm/cold
tiering or rolling-window retention.

```csharp
modelBuilder.Entity<AuditLog>().ShardByDate(a => a.Timestamp, DateShardInterval.Year);

dtde => dtde.AddShards("2023", "2024", "2025");
```

Intervals: `Day`, `Month`, `Quarter`, `Year`. DTDE extracts the
relevant component of the date to pick the shard.

Queries with a `Where(a => a.Timestamp >= start && a.Timestamp < end)`
predicate prune to the date-range-overlapping shards. Queries without
fan out.

Best for: audit logs, time-series metrics, transaction history, any
append-mostly workload.

### `UseManualSharding` — pre-existing tables

Use when shard tables are managed externally (a SQL project, a DBA,
etc.) and you need DTDE to route to specific names but not own the
schema.

```csharp
modelBuilder.Entity<Order>()
    .UseManualSharding(config =>
    {
        config.AddTable("dbo.Orders_2023", o => o.OrderDate.Year == 2023);
        config.AddTable("dbo.Orders_2024", o => o.OrderDate.Year == 2024);
        config.MigrationsEnabled = false;
    });
```

## Storage modes

How a shard physically lives. Configured per shard, not per entity.

### Table-mode (`AddShards("EU", "US", ...)`)

One database, multiple per-shard tables (`Customers_EU`,
`Customers_US`, ...). The default. Cheapest to operate; suitable for
moderate data volumes where the bottleneck is row count, not storage.

```csharp
dtde => dtde.AddShards("EU", "US", "APAC");
```

DTDE rewrites each per-shard `DbContext`'s table name via
`DtdeShardModelCustomizer`. The default pattern is `{Table}_{ShardId}`;
override per entity via `WithTablePattern("custom_{ShardId}_{Table}")`.

### Database-mode (`AddShard(id, connectionString)`)

One database per shard. Real horizontal scaling; data physically
isolated. Use for compliance (data residency), very large datasets, or
when each shard runs on its own server.

```csharp
dtde => dtde
    .AddShard("EU", "Server=eu.db;...")
    .AddShard("US", "Server=us.db;...")
    .AddShard("APAC", "Server=apac.db;...");
```

The entity keeps its base table name (`Customers`) inside each
database; no per-shard table rewriting.

### Mixed-mode (`AddTableShardInDatabase(id, connectionString)`)

Per-shard tables spread across multiple databases. The compromise
between table-mode and database-mode: you get physical isolation
between database boundaries but multiple shards can co-exist in one
database to amortise infrastructure cost.

```csharp
dtde => dtde
    .AddTableShardInDatabase("EU",   "Server=primary.db;...")
    .AddTableShardInDatabase("US",   "Server=primary.db;...")
    .AddTableShardInDatabase("APAC", "Server=secondary.db;...");
```

`primary.db` ends up with `Customers_EU` + `Customers_US`;
`secondary.db` ends up with `Customers_APAC`. DTDE's per-shard
customizer rewrites the table name; the per-shard context factory uses
the right connection.

## Shard groups

If two entities in the same `DbContext` need **different shard
topologies** (eight hash buckets for users *and* three yearly buckets
for orders), bind each entity to a named **shard group**:

```csharp
dtde => dtde
    .AddShardGroup("hash8", g => g.AddShards("0", "1", "2", "3", "4", "5", "6", "7"))
    .AddShardGroup("years", g => g.AddShards("2023", "2024", "2025"));

modelBuilder.Entity<UserProfile>().ShardByHash(u => u.UserId, 8).UseShardGroup("hash8");
modelBuilder.Entity<Order>().ShardByDate(o => o.OrderDate).UseShardGroup("years");
```

**Same local id in different groups means different physical shards.**
Shard `"0"` in `hash8` is a different shard from shard `"0"` in
`hash3`. DTDE keys participants and the durable transaction log by
**fully-qualified id** (`groupName::shardId`) so the two never alias.

The simple "all entities share one shard topology" case stays
configuration-free: `dtde.AddShards("EU", "US", "APAC")` puts every
shard in the implicit default group, and entities that don't call
`UseShardGroup` bind to it implicitly.

A misspelled group name throws at first DbContext use:

```text
InvalidOperationException: Entity 'Order' is bound to shard group 'years',
but no such group is registered. Add it with dtde.AddShardGroup("years", ...)
or remove the UseShardGroup(...) call on the entity.
```

## Query routing

The query executor uses the entity's group to scope fan-out. For a
sharded entity:

| Predicate on shard key | What DTDE does |
|---|---|
| Equality on the shard-key property (and the strategy can map it to a single shard) | **Pruned** — query runs on exactly one shard. |
| Date range that intersects N shards (date strategy only) | Fanned out across the N intersecting shards. |
| No predicate on the shard key | Fanned out across **every** shard in the entity's group. |

For non-sharded entities (no `ShardBy*` annotation), the query goes to
the default group's first hot shard.

## Writes

`SaveChangesAsync` on the parent context triggers the
`TransparentShardingInterceptor`. It groups changes by target shard and
either:

- writes them all to one shard (when only one shard is touched), or
- automatically wraps the write in a cross-shard transaction (2PC,
  when more than one shard is touched).

The fast paths apply equally inside an explicit
`BeginCrossShardTransactionAsync` scope — see
[cross-shard transactions](cross-shard-transactions.md).

For high-throughput insertion, prefer `BulkInsertAsync` —
[bulk operations](bulk-operations.md).

## Provisioning

`db.EnsureAllShardsCreatedAsync()` walks every shard group and, for
each shard:

- **table-mode** — creates the per-shard tables in the parent's
  database via `IRelationalDatabaseCreator.CreateTablesAsync` against
  a per-shard model that has only the in-group entities (out-of-group
  entities are excluded so each shard provisions only what it owns);
- **database-mode** — creates the per-shard database and its tables
  via `EnsureCreatedAsync`;
- **manual mode** — no-op.

Use this for samples, integration tests, and dev environments. In
production, use EF Core migrations per shard.

## Patterns by use case

| Use case | Strategy | Storage | Group? |
|---|---|---|---|
| Multi-region SaaS | `ShardBy(t => t.TenantId)` or `ShardBy(c => c.Region)` | Database-mode | Default |
| Time-series logs / metrics | `ShardByDate(...)` | Table-mode (cheap) or mixed-mode (tiered storage) | Default |
| Even distribution / hot-spot avoidance | `ShardByHash(...)` | Table-mode | Default |
| Mixed: users-by-hash + orders-by-year | `ShardByHash` + `ShardByDate` | Either | **Named groups** |
| Pre-existing tables (DBA-owned schema) | `UseManualSharding(...)` | Manual | Default |

## See also

- [Getting started](getting-started.md) — 5-minute walk-through.
- [Cross-shard transactions](cross-shard-transactions.md) — how
  routing interacts with 2PC.
- [Bulk operations](bulk-operations.md) — set-based fan-out.
- The runnable [samples](https://github.com/yohasacura/dtde/tree/main/samples) — one per strategy.
