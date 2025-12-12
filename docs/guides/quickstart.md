# Quick Start

Get DTDE running in **5 minutes**. This guide shows the fastest path to transparent sharding.

!!! tip "Time Required"
    ⏱️ **5 minutes** for basic setup

---

## Step 1: Install Package

```bash
dotnet add package Dtde.EntityFramework
```

---

## Step 2: Define Your Entity

```csharp
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;  // Shard key
    public decimal Price { get; set; }
}
```

---

## Step 3: Create DbContext

```csharp
using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
using Microsoft.EntityFrameworkCore;

public class AppDbContext : DtdeDbContext
{
    public DbSet<Product> Products => Set<Product>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enable sharding by Category
        modelBuilder.Entity<Product>()
            .ShardBy(p => p.Category);
    }
}
```

---

## Step 4: Configure Services

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
    options.UseDtde(dtde =>
    {
        dtde.AddShard(s => s.WithId("Electronics").WithShardKeyValue("Electronics"));
        dtde.AddShard(s => s.WithId("Clothing").WithShardKeyValue("Clothing"));
        dtde.AddShard(s => s.WithId("Books").WithShardKeyValue("Books"));
    });
});

var app = builder.Build();
```

---

## Step 5: Use It!

```csharp
// Queries are automatically distributed across shards
var allProducts = await _context.Products.ToListAsync();

// Shard-specific queries are optimized (only queries Electronics shard)
var electronics = await _context.Products
    .Where(p => p.Category == "Electronics")
    .ToListAsync();

// Inserts are automatically routed to correct shard
_context.Products.Add(new Product
{
    Name = "Laptop",
    Category = "Electronics",  // Routes to Electronics shard
    Price = 999.99m
});
await _context.SaveChangesAsync();
```

---

## ✅ Done!

Your data is now transparently sharded. DTDE handles:

| Feature | What DTDE Does |
|---------|----------------|
| **Query Routing** | Automatically routes to correct shard(s) |
| **Parallel Execution** | Queries multiple shards simultaneously |
| **Result Merging** | Combines results into unified response |
| **Write Routing** | Routes inserts/updates to correct shard |

---

## Quick Reference

### Sharding Methods

```csharp
// Property-based sharding
entity.ShardBy(e => e.Region);

// Hash-based sharding (even distribution)
entity.ShardByHash(e => e.Id, shardCount: 8);

// Date-based sharding
entity.ShardByDate(e => e.CreatedAt, DateShardInterval.Year);

// Manual sharding (pre-created tables)
entity.UseManualSharding(config => {
    config.AddTable("dbo.Orders_2024", o => o.Year == 2024);
});
```

### Temporal Queries (Optional)

```csharp
// Enable temporal on entity
entity.HasTemporalValidity(e => e.ValidFrom, e => e.ValidTo);

// Query point-in-time (DtdeDbContext method)
var current = await _context.ValidAt<Entity>(DateTime.Today).ToListAsync();

// Get all versions
var history = await _context.AllVersions<Entity>().ToListAsync();
```

---

## What's Next?

<div class="grid cards" markdown>

-   **[Complete Guide](getting-started.md)**

    Full tutorial covering installation, configuration, and all concepts.

-   **[Sharding Guide](sharding-guide.md)**

    Deep dive into property, hash, date, and range sharding strategies.

-   **[Temporal Guide](temporal-guide.md)**

    Point-in-time queries and version tracking.

-   **[API Reference](../wiki/api-reference.md)**

    Complete API documentation.

</div>

---

[← Back to Guides](index.md) | [Complete Guide →](getting-started.md)
