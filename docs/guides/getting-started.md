# Getting started

Get DTDE running in five minutes. By the end you'll have a sharded `DbContext`,
three logical shards, and a working LINQ query that fans out across them.

## Prerequisites

- .NET 8 SDK or newer (the package multi-targets `net8.0`, `net9.0`, `net10.0`).
- An EF Core provider — any will do. This guide uses SQLite because it has zero setup.
- Roughly five minutes.

## 1. Install the package

```bash
dotnet add package Dtde.EntityFramework
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
```

`Dtde.EntityFramework` is the only DTDE package application code references.
It transitively pulls in `Dtde.Core` and `Dtde.Abstractions`.

## 2. Define an entity

Pick any property to shard on. We'll use `Customer.Region`.

```csharp
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty; // shard key
    public DateTime CreatedAt { get; set; }
}
```

DTDE doesn't care what the property is called, what its type is, or where it
sits on the entity. It works with whatever string-ish property you pick — region
codes, tenant ids, customer-segment names, etc.

## 3. Inherit `DtdeDbContext`

Subclass [`DtdeDbContext`](https://github.com/yohasacura/dtde/blob/main/src/Dtde.EntityFramework/DtdeDbContext.cs)
instead of `DbContext`, and configure entities the way you would in any EF Core
app — but with one extra fluent call to declare the shard key:

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
            entity.ShardBy(c => c.Region); // <-- the only DTDE-specific line
        });
    }
}
```

## 4. Wire it up in `Program.cs`

One call. The first lambda configures EF Core (any provider). The second declares
the shards.

```csharp
using Dtde.EntityFramework.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDtdeDbContext<AppDbContext>(
    db => db.UseSqlite("Data Source=app.db"),
    dtde => dtde.AddShards("EU", "US", "APAC"));

var app = builder.Build();

// ... routes / middleware
```

That's the whole setup.

`AddShards("EU", "US", "APAC")` says "I have three logical shards, named EU,
US, and APAC, and the connection string from `db => ...` covers all of them."
Each shard's id doubles as the shard-key value, so a row with `Region = "EU"`
lands in the EU shard.

## 5. Use it

Standard EF Core LINQ. DTDE handles routing.

```csharp
public class CustomersController(AppDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(Customer customer)
    {
        db.Customers.Add(customer);     // routed by Region
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpGet]
    public Task<List<Customer>> AllCustomers()
        => db.Customers.ToListAsync();   // fans out across all shards

    [HttpGet("{region}")]
    public Task<List<Customer>> ByRegion(string region)
        => db.Customers
              .Where(c => c.Region == region)  // partition pruned to one shard
              .ToListAsync();
}
```

That's the entire mental model:

- **Inserts** are routed by the shard key.
- **Queries with a `Where` on the shard key** are pruned to the matching shard.
- **Queries without** fan out across all shards and merge the results.

## What if I need...

### ...separate databases per shard

Pass a connection string per shard:

```csharp
dtde => dtde
    .AddShard("EU",   "Server=eu-db;Database=Customers;...")
    .AddShard("US",   "Server=us-db;Database=Customers;...")
    .AddShard("APAC", "Server=apac-db;Database=Customers;...")
```

### ...time-bucketed sharding for transactions / audit logs

Configure the entity differently and add date-ranged shards:

```csharp
// AppDbContext.OnModelCreating
modelBuilder.Entity<Order>()
    .ShardByDate(o => o.OrderDate, DateShardInterval.Year);

// Program.cs
dtde => dtde
    .AddShard(s => s.WithId("2023").WithConnectionString(year2023ConnStr)
        .WithDateRange(new DateTime(2023, 1, 1), new DateTime(2024, 1, 1)))
    .AddShard(s => s.WithId("2024").WithConnectionString(year2024ConnStr)
        .WithDateRange(new DateTime(2024, 1, 1), new DateTime(2025, 1, 1)));
```

### ...even distribution by hash

```csharp
// AppDbContext.OnModelCreating
modelBuilder.Entity<UserProfile>()
    .ShardByHash(u => u.UserId, shardCount: 8);

// Program.cs
dtde => dtde.AddShards("0", "1", "2", "3", "4", "5", "6", "7");
```

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

See the [Temporal guide](temporal-guide.md) for the full mutation API
(`AddTemporal`, `CreateNewVersion`, `Terminate`).

### ...shard configs from JSON instead of code

For ops scenarios where shards change without redeploying:

```csharp
dtde => dtde.AddShardsFromConfig("shards.json");
```

See [Configuration reference](../wiki/configuration.md) for the JSON schema.

## Single canonical configuration path

DTDE has **one** way to register, **one** place to configure entities, **one**
DbContext base type:

| What | Where | API |
|---|---|---|
| DI registration | `Program.cs` | `services.AddDtdeDbContext<TContext>(db, dtde)` |
| Shard list | inside `dtde` callback | `dtde.AddShards(...)` / `dtde.AddShard(id[, conn])` / `dtde.AddShard(s => ...)` |
| Entity sharding | `OnModelCreating` | `entity.ShardBy(...)` / `ShardByDate(...)` / `ShardByHash(...)` |
| Temporal validity | `OnModelCreating` | `entity.HasTemporalValidity(...)` |
| Queries | application code | standard LINQ + `db.ValidAt<T>(...)` |

If you find yourself reaching for something that's not in this table, check the
[Wiki](../wiki/index.md) — but the table above is enough for ~90% of
applications.

## Next steps

- [Sharding guide](sharding-guide.md) — when each strategy is appropriate.
- [Temporal guide](temporal-guide.md) — bi-temporal versioning, point-in-time queries.
- [Cross-shard transactions](cross-shard-transactions.md) — 2PC across shards.
- [Sample projects](https://github.com/yohasacura/dtde/tree/main/samples) — six runnable apps, one per strategy.
