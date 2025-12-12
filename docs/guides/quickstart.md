# DTDE Quickstart

Get DTDE running in under 5 minutes! This guide shows you the fastest path to transparent sharding.

## ⏱️ Time Required: 5 minutes

---

## Step 1: Install Package (30 seconds)

```bash
dotnet add package Dtde.EntityFramework
```

---

## Step 2: Create DbContext (1 minute)

```csharp
using Dtde.EntityFramework;
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

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;  // Shard key
    public decimal Price { get; set; }
}
```

---

## Step 3: Configure Services (1 minute)

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

## Step 4: Use It! (1 minute)

```csharp
// Queries are automatically distributed across shards
var allProducts = await _context.Products.ToListAsync();

// Shard-specific queries are optimized
var electronics = await _context.Products
    .Where(p => p.Category == "Electronics")
    .ToListAsync();

// Inserts are automatically routed
_context.Products.Add(new Product
{
    Name = "Laptop",
    Category = "Electronics",  // Routes to Electronics shard
    Price = 999.99m
});
await _context.SaveChangesAsync();
```

---

## ✅ You're Done!

Your data is now transparently sharded across multiple tables. DTDE handles:

- ✅ Query routing to correct shard(s)
- ✅ Parallel execution across shards
- ✅ Result merging
- ✅ Insert/Update routing

---

## 🚀 Next Steps

| What | Link |
|------|------|
| **Full setup guide** | [Getting Started](getting-started.md) |
| **Sharding strategies** | [Sharding Guide](sharding-guide.md) |
| **Temporal versioning** | [Temporal Guide](temporal-guide.md) |
| **API reference** | [Wiki](../wiki/index.md) |

---

## Quick Reference

### Sharding Options

```csharp
// Property-based sharding
entity.ShardBy(e => e.Region);

// Hash-based sharding (even distribution)
entity.ShardByHash(e => e.Id, shardCount: 8);

// Date-based sharding
entity.ShardByDate(e => e.CreatedAt, DateInterval.Year);
```

### Temporal Queries (Optional)

```csharp
// Enable temporal on entity
entity.HasTemporalValidity(e => e.ValidFrom, e => e.ValidTo);

// Query point-in-time
var current = await _context.ValidAt<Entity>(DateTime.Today).ToListAsync();
```

---

[← Back to Guides](index.md) | [Full Getting Started →](getting-started.md)
