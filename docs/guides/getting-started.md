# Getting Started

This comprehensive guide walks you through setting up DTDE in your .NET application. By the end, you'll understand how to configure transparent sharding and optional temporal versioning.

!!! info "Prerequisites"
    - **.NET 8.0 / 9.0 / 10.0 SDK** or later
    - **SQL Server** (local, Azure SQL, or SQL Server Express)
    - Basic knowledge of **Entity Framework Core**
    - An IDE (Visual Studio, VS Code, or Rider)

---

## Installation

```bash
# Using .NET CLI (recommended)
dotnet add package Dtde.EntityFramework

# Using Package Manager Console
Install-Package Dtde.EntityFramework
```

DTDE automatically includes:

- `Dtde.Abstractions` - Core interfaces
- `Dtde.Core` - Core implementations
- `Microsoft.EntityFrameworkCore` (8.0+ / 9.0+ / 10.0+)

---

## Basic Setup

### 1. Create Your DbContext

Inherit from `DtdeDbContext` instead of `DbContext`:

```csharp
using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
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
        base.OnModelCreating(modelBuilder);  // Important: call base!

        // Entity configurations go here
    }
}
```

### 2. Configure Services

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
    options.UseDtde();  // Enable DTDE with default options
});

var app = builder.Build();
```

### 3. Add Connection String

```json title="appsettings.json"
{
  "ConnectionStrings": {
    "Default": "Server=localhost;Database=MyApp;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

---

## Your First Sharded Entity

### Define the Entity

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
        // Creates: Customers_EU, Customers_US, Customers_APAC
    });
}
```

### Define Shards

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
    options.UseDtde(dtde =>
    {
        dtde.AddShard(s => s
            .WithId("EU")
            .WithShardKeyValue("EU")
            .WithTier(ShardTier.Hot));

        dtde.AddShard(s => s
            .WithId("US")
            .WithShardKeyValue("US")
            .WithTier(ShardTier.Hot));

        dtde.AddShard(s => s
            .WithId("APAC")
            .WithShardKeyValue("APAC")
            .WithTier(ShardTier.Hot));
    });
});
```

### Alternative: JSON Configuration

```json title="shards.json"
{
  "shards": [
    {
      "shardId": "EU",
      "name": "Customers_EU",
      "shardKeyValue": "EU",
      "tier": "Hot"
    },
    {
      "shardId": "US",
      "name": "Customers_US",
      "shardKeyValue": "US",
      "tier": "Hot"
    }
  ]
}
```

```csharp
options.UseDtde(dtde => dtde.AddShardsFromConfig("shards.json"));
```

---

## Querying Sharded Data

### Transparent Queries

Write standard EF Core LINQ - DTDE handles distribution:

```csharp
public class CustomerService
{
    private readonly AppDbContext _context;

    public CustomerService(AppDbContext context) => _context = context;

    // Queries ALL shards, merges results
    public async Task<List<Customer>> GetAllCustomersAsync()
    {
        return await _context.Customers.ToListAsync();
    }

    // Optimized: only queries EU shard
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
            .Where(c => c.Name.Contains(searchTerm))
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
    // DTDE routes to correct shard based on Region
    _context.Customers.Add(customer);
    await _context.SaveChangesAsync();
    return customer;
}
```

---

## Adding Temporal Versioning

Temporal versioning is **optional**. Add it when you need to track entity history.

### Define Temporal Entity

Add validity properties (use any names you want):

```csharp
public class Contract
{
    public int Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CustomerName { get; set; } = string.Empty;

    // Temporal properties - any names work!
    public DateTime EffectiveDate { get; set; }
    public DateTime? ExpirationDate { get; set; }  // null = currently valid
}
```

### Configure Temporal Validity

```csharp
modelBuilder.Entity<Contract>(entity =>
{
    entity.HasKey(c => c.Id);

    // Optional: combine with sharding
    entity.ShardByDate(c => c.EffectiveDate, DateShardInterval.Year);

    // Configure temporal validity
    entity.HasTemporalValidity(
        validFrom: c => c.EffectiveDate,
        validTo: c => c.ExpirationDate);
});
```

### Query Temporal Data

Use `DtdeDbContext` methods for temporal queries:

```csharp
// Get contracts valid today
var currentContracts = await _context.ValidAt<Contract>(DateTime.Today)
    .ToListAsync();

// Get contracts valid during Q1 2024
var q1Contracts = await _context.ValidBetween<Contract>(
    new DateTime(2024, 1, 1),
    new DateTime(2024, 3, 31))
    .ToListAsync();

// Get ALL versions (bypasses temporal filtering)
var history = await _context.AllVersions<Contract>()
    .Where(c => c.ContractNumber == "CTR-001")
    .OrderBy(c => c.EffectiveDate)
    .ToListAsync();
```

### Temporal Write Operations

```csharp
// Add new entity with effective date
_context.AddTemporal(contract, effectiveFrom: DateTime.Today);

// Create new version (closes old, opens new)
var newVersion = _context.CreateNewVersion(
    existingContract,
    changes: c => c.Amount = 50000m,
    effectiveDate: DateTime.Today);

// Terminate entity
_context.Terminate(contract, terminationDate: DateTime.Today);

await _context.SaveChangesAsync();
```

---

## Complete Example

```csharp title="Program.cs"
using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
    options.UseDtde(dtde =>
    {
        // Configure shards
        dtde.AddShard(s => s.WithId("EU").WithShardKeyValue("EU").WithTier(ShardTier.Hot));
        dtde.AddShard(s => s.WithId("US").WithShardKeyValue("US").WithTier(ShardTier.Hot));

        // Performance settings
        dtde.SetMaxParallelShards(10);
        dtde.EnableDiagnostics();
    });
});

var app = builder.Build();
app.Run();
```

```csharp title="AppDbContext.cs"
public class AppDbContext : DtdeDbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Contract> Contracts => Set<Contract>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Sharded entity
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ShardBy(c => c.Region);
        });

        // Sharded + Temporal entity
        modelBuilder.Entity<Contract>(entity =>
        {
            entity.ShardByDate(c => c.EffectiveDate, DateShardInterval.Year);
            entity.HasTemporalValidity(c => c.EffectiveDate, c => c.ExpirationDate);
        });
    }
}
```

---

## Next Steps

<div class="grid cards" markdown>

-   **[Sharding Guide](sharding-guide.md)**

    Deep dive into all sharding strategies: property, hash, date, manual.

-   **[Temporal Guide](temporal-guide.md)**

    Complete guide to temporal versioning and point-in-time queries.

-   **[API Reference](../wiki/api-reference.md)**

    Complete API documentation for all classes and methods.

-   **[Samples](https://github.com/yohasacura/dtde/tree/main/samples)**

    Working examples for each sharding strategy.

</div>

---

## Troubleshooting

??? question "My queries return empty results"
    - Ensure shards are properly configured and match your data
    - Check that shard key values match exactly (case-sensitive)
    - Verify database tables exist

??? question "Performance is slow"
    - Review `MaxParallelShards` setting
    - Enable diagnostics: `dtde.EnableDiagnostics()`
    - Check that shard predicates optimize queries

??? question "Getting 'No shards found' errors"
    - Verify shard registry contains matching shards
    - Check entity is configured for sharding in `OnModelCreating`

For more help, see the [Troubleshooting Guide](../wiki/troubleshooting.md).

---

[← Back to Guides](index.md) | [Sharding Guide →](sharding-guide.md)
