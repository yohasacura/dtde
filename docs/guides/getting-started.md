# Getting started

Get DTDE running in five minutes. By the end you'll have a sharded `DbContext`,
three logical shards backed by real per-shard tables, and a working LINQ query
that fans out across them.

## Prerequisites

- .NET 8 SDK or newer (the package multi-targets `net8.0`, `net9.0`, `net10.0`).
- An EF Core provider — any will do. This guide uses SQLite because it has zero setup.
- About five minutes.

## How DTDE actually shards your data

Two layers, one contract:

1. **Per entity (in `OnModelCreating`)** you declare *how* a row maps to a shard
   key — `entity.ShardBy(c => c.Region)` for property-value sharding,
   `ShardByDate(...)`, `ShardByHash(...)`, etc.
2. **In `Program.cs`** you declare *the shards themselves* — `dtde.AddShards("EU", "US", "APAC")`
   for table-mode (one DB, many tables) or
   `dtde.AddShard("EU", "Server=eu-db;...")` for database-mode (a separate DB
   per shard).

**The contract that ties them together:** the shard's id (the string you pass
to `AddShard("EU")` or `AddShards(...)`) must equal the value the entity's
shard-key property carries at runtime. So a `Customer` row with
`Region = "EU"` lands in the shard registered with id `"EU"`. If the property
value doesn't match any registered shard, the row has nowhere to go and DTDE
will throw at write time.

That's the whole mental model. The rest is just choosing where shards live.

## 1. Install the package

```bash
dotnet add package Dtde.EntityFramework
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
```

`Dtde.EntityFramework` transitively pulls in `Dtde.Core` and `Dtde.Abstractions`.

## 2. Define an entity

```csharp
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty; // shard-key property
    public DateTime CreatedAt { get; set; }
}
```

Any string-ish property works — region codes, tenant ids, whatever your
domain calls it. DTDE makes no assumption about the name or content.

## 3. Inherit `DtdeDbContext`, declare the shard key in `OnModelCreating`

```csharp
using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
using Microsoft.EntityFrameworkCore;

public class AppDbContext : DtdeDbContext
{
    public DbSet<Customer> Customers => Set<Customer>();

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.ShardBy(c => c.Region);   // <-- the only DTDE-specific line
        });
    }
}
```

`ShardBy(c => c.Region)` says "use the value of `Region` as the shard key".
That's all the model needs to know.

## 4. Wire it up in `Program.cs`

One call, two callbacks:

```csharp
using Dtde.EntityFramework.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDtdeDbContext<AppDbContext>(
    (db, conn) => db.UseSqlite(conn ?? "Data Source=app.db"),
    dtde => dtde.AddShards("EU", "US", "APAC"));

var app = builder.Build();
```

What's happening:

- The first lambda — `(db, conn) => db.UseSqlite(conn ?? "...")` — configures
  the EF Core provider. DTDE invokes it twice: once for the parent context
  (with `conn = null` → fall through to your default) and once per shard
  (with `conn` set to that shard's connection string).
- The second lambda — `dtde => ...` — declares your shards.

Above is **table-mode**: one SQLite file, three logical shards, three tables
(`Customers_EU`, `Customers_US`, `Customers_APAC`) created automatically by
DTDE's per-shard model.

Want **database-mode** (one database per shard)? Same call, you just supply
each shard's connection string:

```csharp
builder.Services.AddDtdeDbContext<AppDbContext>(
    (db, conn) => db.UseSqlite(conn ?? "Data Source=base.db"),
    dtde => dtde
        .AddShard("EU", "Data Source=eu.db")
        .AddShard("US", "Data Source=us.db")
        .AddShard("APAC", "Data Source=apac.db"));
```

Each per-shard context now connects to its own database; the table is named
`Customers` everywhere because there's no name conflict — different DB.

Want **mixed mode** — per-shard tables spread across multiple databases (e.g.
EU and US tables in one regional DB, APAC tables in another)? Use the
`AddTableShardInDatabase` helper:

```csharp
builder.Services.AddDtdeDbContext<AppDbContext>(
    (db, conn) => db.UseSqlite(conn ?? "Data Source=base.db"),
    dtde => dtde
        .AddTableShardInDatabase("EU",   "Data Source=primary.db")
        .AddTableShardInDatabase("US",   "Data Source=primary.db")
        .AddTableShardInDatabase("APAC", "Data Source=secondary.db"));
```

`primary.db` ends up with `Customers_EU` + `Customers_US`; `secondary.db`
ends up with `Customers_APAC`. Each shard gets its own per-shard table
(table-mode rewriting still applies), but the tables are spread across the
databases you choose.

## 5. Provision the shards (one-time)

For samples, integration tests, or a fresh dev environment, call:

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.EnsureAllShardsCreatedAsync();
}
```

This walks every registered shard and creates its tables (table-mode) or its
database + tables (database-mode). In production you'd usually run EF Core
migrations per shard instead.

## 6. Use it

Standard EF Core LINQ:

```csharp
public class CustomersController(AppDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(Customer customer)
    {
        db.Customers.Add(customer);     // routed by Customer.Region
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpGet]
    public Task<List<Customer>> All() => db.Customers.ToListAsync();
    // No filter on shard key → fans out across all shards, merges results.

    [HttpGet("{region}")]
    public Task<List<Customer>> ByRegion(string region) =>
        db.Customers.Where(c => c.Region == region).ToListAsync();
    // Where on shard key → DTDE prunes to the matching shard only.
}
```

Three rules, summarised:

- **Inserts**: routed by the shard-key value on the entity.
- **Queries with `Where` on the shard key**: pruned to one shard.
- **Queries without**: fanned out across all shards, results merged.

## What if I need...

### ...time-bucketed sharding (transactions, audit logs)

```csharp
// Entity
modelBuilder.Entity<Order>()
    .ShardByDate(o => o.OrderDate, DateShardInterval.Year);

// Program.cs — one shard per year
dtde => dtde.AddShards("2023", "2024", "2025")
```

The order's year (extracted from `o.OrderDate`) becomes the shard key.

### ...hash-based even distribution

```csharp
modelBuilder.Entity<UserProfile>()
    .ShardByHash(u => u.UserId, shardCount: 8);

dtde => dtde.AddShards("0", "1", "2", "3", "4", "5", "6", "7")
```

DTDE computes the user-id hash modulo 8 to pick the shard.

### ...point-in-time queries (temporal entities)

Add `ValidFrom`/`ValidTo` properties on the entity, declare them in
`OnModelCreating`, then query with `db.ValidAt<T>(date)`:

```csharp
public class Contract
{
    public int Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
}

// AppDbContext.OnModelCreating
modelBuilder.Entity<Contract>()
    .HasTemporalValidity(c => c.ValidFrom, c => c.ValidTo);

// Querying
var asOfLastMonth = await db
    .ValidAt<Contract>(DateTime.UtcNow.AddMonths(-1))
    .ToListAsync();
```

### ...customising the per-shard table-name pattern

Default is `{Table}_{ShardId}` (so `Customers_EU`). Override per entity:

```csharp
modelBuilder.Entity<Customer>()
    .ShardBy(c => c.Region)
    .WithTablePattern("{Table}__shard_{ShardId}");   // → Customers__shard_EU
```

Tokens: `{Table}` (entity table name), `{Schema}` (default `dbo`),
`{ShardId}`.

### ...shard configs from JSON instead of code

```csharp
dtde => dtde.AddShardsFromConfig("shards.json");
```

JSON schema in [Configuration reference](../wiki/configuration.md).

## The single canonical map

| What | Where | API |
|---|---|---|
| DI registration | `Program.cs` | `services.AddDtdeDbContext<TContext>((db, conn) => ..., dtde => ...)` |
| Provisioning | startup | `db.EnsureAllShardsCreatedAsync()` |
| Shard list | inside `dtde` callback | `dtde.AddShards(...)` / `dtde.AddShard(id[, conn])` / `dtde.AddShard(s => ...)` |
| Entity sharding | `OnModelCreating` | `entity.ShardBy(...)` / `ShardByDate(...)` / `ShardByHash(...)` |
| Per-shard table pattern | `OnModelCreating` | `entity.ShardBy(...).WithTablePattern("{Table}_{ShardId}")` |
| Temporal validity | `OnModelCreating` | `entity.HasTemporalValidity(...)` |
| Queries | application code | standard LINQ + `db.ValidAt<T>(...)` |

## Next steps

- [Sharding guide](sharding-guide.md) — when each strategy is appropriate.
- [Temporal guide](temporal-guide.md) — bi-temporal versioning, point-in-time queries.
- [Cross-shard transactions](cross-shard-transactions.md) — 2PC across shards.
- [Sample projects](https://github.com/yohasacura/dtde/tree/main/samples) — six runnable apps, one per strategy.
