# Getting Started with DTDE

This guide walks you through setting up DTDE (Distributed Temporal Data Engine) in your .NET application. By the end, you'll understand how to configure transparent sharding and optional temporal versioning for your Entity Framework Core entities.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Installation](#installation)
- [Basic Setup](#basic-setup)
- [Your First Sharded Entity](#your-first-sharded-entity)
- [Querying Sharded Data](#querying-sharded-data)
- [Adding Temporal Versioning](#adding-temporal-versioning)
- [Next Steps](#next-steps)

---

## Prerequisites

Before you begin, ensure you have:

- **.NET 9.0 SDK** or later
- **SQL Server** (local, Azure SQL, or SQL Server Express)
- Basic knowledge of **Entity Framework Core**
- An IDE (Visual Studio, VS Code, or Rider)

---

## Installation

### Package Installation

Install the DTDE NuGet package:

```bash
# Using .NET CLI
dotnet add package Dtde.EntityFramework

# Using Package Manager Console
Install-Package Dtde.EntityFramework
```

### Package Dependencies

DTDE automatically includes these dependencies:
- `Microsoft.EntityFrameworkCore` (9.0+)
- `Microsoft.EntityFrameworkCore.SqlServer` (9.0+)

---

## Basic Setup

### Step 1: Create Your DbContext

Inherit from `DtdeDbContext` instead of `DbContext`:

```csharp
using Dtde.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace MyApp.Data;

public class AppDbContext : DtdeDbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Entity configurations go here
    }
}
```

### Step 2: Configure Services

Register DTDE in your application:

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
    options.UseDtde(); // Enable DTDE with default options
});

var app = builder.Build();
```

### Step 3: Configure Connection String

Add your connection string to `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "Default": "Server=localhost;Database=MyApp;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

---

## Your First Sharded Entity

### Define Your Entity

Create a simple entity with a property suitable for sharding:

```csharp
namespace MyApp.Domain;

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;  // Shard key
    public DateTime CreatedAt { get; set; }
}
```

### Configure Sharding

In your DbContext, configure sharding for the entity:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    modelBuilder.Entity<Customer>(entity =>
    {
        entity.HasKey(c => c.Id);

        // Shard by Region property
        entity.ShardBy(c => c.Region)
              .WithStorageMode(ShardStorageMode.Tables);
        // Creates: Customers_EU, Customers_US, Customers_APAC, etc.
    });
}
```

### Configure Shards

Define your shard configuration:

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
    options.UseDtde(dtde =>
    {
        // Add shard definitions
        dtde.AddShard(s => s
            .WithId("EU")
            .WithShardKeyValue("EU")
            .WithTable("Customers_EU", "dbo")
            .WithTier(ShardTier.Hot));

        dtde.AddShard(s => s
            .WithId("US")
            .WithShardKeyValue("US")
            .WithTable("Customers_US", "dbo")
            .WithTier(ShardTier.Hot));

        dtde.AddShard(s => s
            .WithId("APAC")
            .WithShardKeyValue("APAC")
            .WithTable("Customers_APAC", "dbo")
            .WithTier(ShardTier.Hot));
    });
});
```

### Alternative: JSON Configuration

Create a `shards.json` file:

```json
{
  "shards": [
    {
      "shardId": "EU",
      "name": "Customers_EU",
      "shardKeyValue": "EU",
      "tableName": "Customers_EU",
      "tier": "Hot"
    },
    {
      "shardId": "US",
      "name": "Customers_US",
      "shardKeyValue": "US",
      "tableName": "Customers_US",
      "tier": "Hot"
    }
  ]
}
```

Load it in configuration:

```csharp
options.UseDtde(dtde =>
{
    dtde.AddShardsFromConfig("shards.json");
});
```

---

## Querying Sharded Data

### Transparent Queries

DTDE handles sharding transparently. Write standard EF Core LINQ queries:

```csharp
public class CustomerService
{
    private readonly AppDbContext _context;

    public CustomerService(AppDbContext context)
    {
        _context = context;
    }

    // This query automatically executes across all shards
    public async Task<List<Customer>> GetAllCustomersAsync()
    {
        return await _context.Customers.ToListAsync();
    }

    // This query is optimized to only hit the EU shard
    public async Task<List<Customer>> GetEuropeanCustomersAsync()
    {
        return await _context.Customers
            .Where(c => c.Region == "EU")
            .ToListAsync();
    }

    // Complex queries work across shards
    public async Task<List<Customer>> SearchCustomersAsync(string searchTerm)
    {
        return await _context.Customers
            .Where(c => c.Name.Contains(searchTerm) || c.Email.Contains(searchTerm))
            .OrderBy(c => c.Name)
            .Take(100)
            .ToListAsync();
    }
}
```

### Insert Operations

Inserts are automatically routed to the correct shard:

```csharp
public async Task<Customer> CreateCustomerAsync(Customer customer)
{
    // DTDE automatically routes to correct shard based on Region
    _context.Customers.Add(customer);
    await _context.SaveChangesAsync();
    return customer;
}
```

---

## Adding Temporal Versioning

Temporal versioning is **optional**. Add it when you need to track entity history.

### Update Your Entity

Add temporal validity properties:

```csharp
public class Contract
{
    public int Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int Year { get; set; }  // Shard key

    // Temporal properties (any names work)
    public DateTime EffectiveDate { get; set; }
    public DateTime? ExpirationDate { get; set; }
}
```

### Configure Temporal Validity

```csharp
modelBuilder.Entity<Contract>(entity =>
{
    entity.HasKey(c => c.Id);

    // Configure sharding
    entity.ShardByDate(c => c.EffectiveDate, DateInterval.Year)
          .WithStorageMode(ShardStorageMode.Tables);

    // Configure temporal validity
    entity.HasTemporalValidity(
        validFrom: c => c.EffectiveDate,
        validTo: c => c.ExpirationDate);
});
```

### Query Temporal Data

```csharp
// Get contracts valid today
var currentContracts = await _context.ValidAt<Contract>(DateTime.Today)
    .ToListAsync();

// Get contracts valid in Q1 2024
var q1Contracts = await _context.ValidBetween<Contract>(
    new DateTime(2024, 1, 1),
    new DateTime(2024, 3, 31))
    .ToListAsync();

// Get all versions of a specific contract
var history = await _context.AllVersions<Contract>()
    .Where(c => c.ContractNumber == "CTR-001")
    .OrderBy(c => c.EffectiveDate)
    .ToListAsync();
```

---

## Next Steps

Now that you have DTDE set up, explore these advanced topics:

### Learn More About Sharding
- [Sharding Guide](sharding-guide.md) - Detailed sharding strategies
- [Hash Sharding](sharding-guide.md#hash-sharding) - Even data distribution
- [Date Sharding](sharding-guide.md#date-sharding) - Time-based partitioning

### Learn More About Temporal Features
- [Temporal Guide](temporal-guide.md) - Complete temporal versioning guide
- [Version Modes](temporal-guide.md#versioning-modes) - Soft delete, audit trail, append-only

### Advanced Configuration
- [Configuration Reference](../wiki/configuration.md) - All configuration options
- [API Reference](../wiki/api-reference.md) - Complete API documentation

### Explore Examples
- [Hash Sharding Sample](/samples/Dtde.Samples.HashSharding/) - Even distribution example
- [Date Sharding Sample](/samples/Dtde.Samples.DateSharding/) - Time-based sharding
- [Region Sharding Sample](/samples/Dtde.Samples.RegionSharding/) - Geographic sharding
- [Multi-Tenant Sample](/samples/Dtde.Samples.MultiTenant/) - SaaS multi-tenancy

---

## Troubleshooting

### Common Issues

**Q: My queries are returning empty results**
- Ensure shards are properly configured and match your data
- Check that shard key values match exactly (case-sensitive by default)
- Verify database tables exist

**Q: Performance is slow**
- Review `MaxParallelShards` setting
- Enable diagnostics: `dtde.EnableDiagnostics()`
- Check that shard predicates optimize queries

**Q: Getting "No shards found" errors**
- Verify shard registry contains matching shards
- Check entity is configured for sharding in `OnModelCreating`

For more help, see the [Troubleshooting Guide](../wiki/troubleshooting.md).

---

[← Back to Guides](README.md) | [Next: Sharding Guide →](sharding-guide.md)
