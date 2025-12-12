# DTDE Development Plan - Revised Architecture

[← Back to Overview](01-overview.md) | [Next: Core Domain Model →](02-core-domain-model.md)

---

## Document Purpose

This document describes the **revised architecture** for DTDE, clarifying the core design principles and separating concerns between:

1. **Transparent Sharding** - The primary feature (always active)
2. **Temporal Versioning** - An optional feature (opt-in per entity)

---

## 1. Core Design Principles

### 1.1 Primary Principle: Transparent Sharding

> **DTDE is primarily a sharding library that makes distributed data appear as a single DbSet.**

The library intercepts EF Core queries and transparently routes them to multiple underlying shards (tables or databases), merging results seamlessly.

```csharp
// Developer writes this:
var customers = await db.Customers.Where(c => c.Region == "EU").ToListAsync();

// DTDE transparently queries:
// - Customers_Shard_EU (if table-based sharding)
// - Server_EU.Customers (if database-based sharding)
// And merges results into a single list
```

### 1.2 Secondary Principle: Optional Temporal Versioning

> **Temporal versioning is completely optional and independent of sharding.**

Developers can:
- Use **sharding only** (no temporal features)
- Use **temporal only** (single database, version tracking)
- Use **both together** (sharded temporal data)
- Use **neither** (regular EF Core entities alongside DTDE entities)

### 1.3 Property Agnostic Design

> **No hardcoded property names.** Everything is configurable.

- Shard keys can be ANY property (Id, Name, Region, Date, etc.)
- Temporal properties can be ANY DateTime properties
- Sharding expressions can use ANY logic (LINQ, custom functions)

---

## 2. Sharding Architecture

### 2.1 Shard Storage Modes

DTDE supports two storage modes for shards:

| Mode | Description | Use Case |
|------|-------------|----------|
| **Table Sharding** | Multiple tables in the same database | Simpler deployment, single connection |
| **Database Sharding** | Multiple databases (same or different servers) | True horizontal scaling, isolation |

```csharp
// Table sharding (same database)
dtde.ConfigureSharding<Customer>(options =>
{
    options.StorageMode = ShardStorageMode.Tables;
    options.TableNamingPattern = "Customers_{ShardKey}"; // Customers_EU, Customers_US
});

// Database sharding (different databases)
dtde.ConfigureSharding<Customer>(options =>
{
    options.StorageMode = ShardStorageMode.Databases;
    options.AddDatabase("EU", "Server=eu.db.com;Database=Customers;...");
    options.AddDatabase("US", "Server=us.db.com;Database=Customers;...");
});

// Manual table names (for sqlproj / pre-created tables)
dtde.ConfigureSharding<Customer>(options =>
{
    options.StorageMode = ShardStorageMode.Tables;
    options.UseExplicitTables(new[]
    {
        "dbo.Customers_2023",
        "dbo.Customers_2024",
        "archive.Customers_Historical"
    });
});
```

### 2.2 Sharding Strategies

| Strategy | Description | Configuration |
|----------|-------------|---------------|
| **Property-Based** | Shard by any property value | `ShardBy(c => c.Region)` |
| **Hash-Based** | Distribute evenly by hash | `ShardByHash(c => c.Id, shardCount: 4)` |
| **Range-Based** | Shard by value ranges | `ShardByRange(c => c.Id, ranges)` |
| **Date-Based** | Shard by date periods | `ShardByDate(c => c.CreatedAt, DateShardInterval.Year)` |
| **Alphabetic** | Shard by first letter(s) | `ShardByAlphabet(c => c.Name, "A-M", "N-Z")` |
| **Row Count** | Auto-create shards at row limits | `ShardByRowCount(maxRows: 1_000_000)` |
| **Expression** | Custom LINQ-based logic | `ShardBy(c => ComputeShard(c))` |
| **Manual** | Explicit shard assignment | `UseExplicitShards(mapping)` |

```csharp
// Property-based (Region)
entity.ShardBy(c => c.Region);

// Hash-based (evenly distributed)
entity.ShardByHash(c => c.Id, shardCount: 8);

// Date-based (yearly tables)
entity.ShardByDate(c => c.CreatedAt, DateShardInterval.Year);

// Alphabetic (A-M, N-Z)
entity.ShardByAlphabet(c => c.LastName, new[] { "A-M", "N-Z" });

// Custom expression
entity.ShardBy(c => c.Amount > 10000 ? "HighValue" : "Standard");

// Row count limit (auto-rotate)
entity.ShardByRowCount(maxRowsPerShard: 500_000);
```

### 2.3 Manual Shard Configuration (sqlproj Support)

For scenarios where tables are created outside of EF migrations:

```csharp
// Explicit table list (pre-created tables)
dtde.ConfigureEntity<Order>(options =>
{
    options.StorageMode = ShardStorageMode.Tables;
    options.MigrationsEnabled = false; // DTDE won't try to create tables
    
    // Explicit shard definitions
    options.AddTableShard("orders_2023", shard =>
    {
        shard.TableName = "dbo.Orders_2023";
        shard.Predicate = o => o.OrderDate.Year == 2023;
    });
    
    options.AddTableShard("orders_2024", shard =>
    {
        shard.TableName = "dbo.Orders_2024";
        shard.Predicate = o => o.OrderDate.Year == 2024;
    });
    
    options.AddTableShard("orders_current", shard =>
    {
        shard.TableName = "dbo.Orders_Current";
        shard.Predicate = o => o.OrderDate.Year >= 2025;
        shard.IsWritable = true; // Only this shard accepts inserts
    });
});
```

### 2.4 Shard Naming Patterns

```csharp
// Pattern-based naming
options.TableNamingPattern = "{EntityName}_{ShardKey}";      // Orders_2024
options.TableNamingPattern = "{EntityName}_Shard_{Index}";   // Orders_Shard_1
options.TableNamingPattern = "{Schema}.{EntityName}_{Key}";  // dbo.Orders_EU

// Dynamic pattern resolution
options.TableNameResolver = (entity, shardKey) => 
    $"archive_{DateTime.Now.Year}.{entity.Name}_{shardKey}";
```

---

## 3. Temporal Versioning (Optional Feature)

### 3.1 Opt-In Temporal Support

Temporal versioning is **completely optional**. Entities without temporal configuration behave exactly like standard EF Core entities.

```csharp
// Entity WITHOUT temporal - behaves like regular EF Core
modelBuilder.Entity<Customer>(entity =>
{
    entity.ShardBy(c => c.Region); // Sharding only, no versioning
});

// Entity WITH temporal - enables version tracking
modelBuilder.Entity<Contract>(entity =>
{
    entity.ShardBy(c => c.EffectiveDate);
    entity.HasTemporalValidity(
        validFrom: c => c.EffectiveDate,
        validTo: c => c.ExpirationDate);
});
```

### 3.2 Temporal Behavior Modes

```csharp
// Mode 1: No temporal (default) - standard EF behavior
// Updates overwrite existing records
entity.WithoutTemporalVersioning();

// Mode 2: Soft versioning - ValidTo is set, old record remains
// Updates close the old record and create a new one
entity.HasTemporalValidity(c => c.ValidFrom, c => c.ValidTo)
      .WithVersioningMode(TemporalVersioningMode.SoftVersion);

// Mode 3: Audit trail - old records are copied to history table
entity.HasTemporalValidity(c => c.ValidFrom, c => c.ValidTo)
      .WithVersioningMode(TemporalVersioningMode.AuditTrail)
      .WithHistoryTable("ContractHistory");
```

### 3.3 Property-Agnostic Temporal Configuration

```csharp
// Any property names work
entity.HasTemporalValidity(
    validFrom: "EffectiveDate",      // string-based
    validTo: "ExpirationDate");

entity.HasTemporalValidity(
    validFrom: e => e.StartDate,     // expression-based
    validTo: e => e.EndDate);

// Open-ended (no end date property)
entity.HasTemporalValidity(validFrom: e => e.CreatedAt);

// Custom open-ended value
entity.HasTemporalValidity(e => e.ValidFrom, e => e.ValidTo)
      .WithOpenEndedValue(new DateTime(9999, 12, 31));
```

---

## 4. Query Behavior

### 4.1 Sharded Queries (Always Active)

```csharp
// DTDE intercepts and distributes across shards
var orders = await db.Orders
    .Where(o => o.Status == "Pending")
    .ToListAsync();

// Internally executes:
// 1. Query all relevant shards in parallel
// 2. Merge results
// 3. Return unified collection
```

### 4.2 Temporal Queries (When Configured)

```csharp
// Only available for entities with HasTemporalValidity
var contracts = await db.Contracts
    .ValidAt(DateTime.Today)
    .ToListAsync();

// Get all versions
var history = await db.Contracts
    .AllVersions()
    .Where(c => c.ContractNumber == "CTR-001")
    .ToListAsync();
```

### 4.3 Standard EF Queries (Always Work)

```csharp
// Direct DbSet access bypasses temporal filtering
// but still uses sharding
var allOrders = await db.Orders.ToListAsync();

// Standard LINQ works normally
var summary = await db.Orders
    .GroupBy(o => o.Region)
    .Select(g => new { Region = g.Key, Total = g.Sum(o => o.Amount) })
    .ToListAsync();
```

---

## 5. Write Behavior

### 5.1 Non-Temporal Entities (Default)

Standard EF Core behavior - DTDE only handles shard routing:

```csharp
var customer = new Customer { Name = "Acme", Region = "EU" };
db.Customers.Add(customer);
await db.SaveChangesAsync();
// → Inserted into Customers_EU (or EU database)

customer.Name = "Acme Corp";
await db.SaveChangesAsync();
// → Updated in place (standard EF behavior)
```

### 5.2 Temporal Entities (When Configured)

Version-bump semantics:

```csharp
var contract = await db.Contracts.ValidAt(DateTime.Today).FirstAsync();
contract.Amount = 50000;
await db.SaveChangesAsync();
// → Old version closed (ValidTo = now)
// → New version created (ValidFrom = now, ValidTo = null)
```

---

## 6. Migration Support

### 6.1 EF Migrations (Automatic)

```csharp
// DTDE can generate migrations for shard tables
dtde.ConfigureSharding<Order>(options =>
{
    options.MigrationsEnabled = true; // Default
    options.StorageMode = ShardStorageMode.Tables;
    options.ShardBy(o => o.Year);
});

// Migration generates:
// CREATE TABLE Orders_2024 (...)
// CREATE TABLE Orders_2025 (...)
```

### 6.2 Manual Tables (sqlproj/DacPac)

```csharp
// Disable migrations, use pre-created tables
dtde.ConfigureSharding<Order>(options =>
{
    options.MigrationsEnabled = false;
    options.StorageMode = ShardStorageMode.Tables;
    options.UseExplicitTables(new[]
    {
        "dbo.Orders_2023",
        "dbo.Orders_2024",
        "dbo.Orders_Current"
    });
});
```

### 6.3 Runtime Table Discovery

```csharp
// Auto-discover tables matching pattern
dtde.ConfigureSharding<Order>(options =>
{
    options.MigrationsEnabled = false;
    options.DiscoverTablesAtRuntime = true;
    options.TableDiscoveryPattern = "Orders_%"; // SQL LIKE pattern
});
```

---

## 7. Configuration Summary

### 7.1 Minimal Configuration (Sharding Only)

```csharp
builder.Services.AddDtdeDbContext<AppDbContext>(
    dbOptions => dbOptions.UseSqlServer(connectionString),
    dtdeOptions =>
    {
        dtdeOptions.ConfigureEntity<Order>(e => e.ShardBy(o => o.Year));
    });
```

### 7.2 Full Configuration

```csharp
builder.Services.AddDtdeDbContext<AppDbContext>(
    dbOptions => dbOptions.UseSqlServer(connectionString),
    dtdeOptions =>
    {
        // Sharded entity without temporal
        dtdeOptions.ConfigureEntity<Customer>(e =>
        {
            e.ShardBy(c => c.Region);
            e.StorageMode = ShardStorageMode.Databases;
            e.AddDatabase("EU", euConnectionString);
            e.AddDatabase("US", usConnectionString);
        });
        
        // Sharded entity with temporal
        dtdeOptions.ConfigureEntity<Contract>(e =>
        {
            e.ShardByDate(c => c.EffectiveDate, DateShardInterval.Year);
            e.HasTemporalValidity(c => c.EffectiveDate, c => c.ExpirationDate);
            e.StorageMode = ShardStorageMode.Tables;
        });
        
        // Regular entity (no DTDE features)
        // Just don't configure it - works as standard EF Core
    });
```

---

## 8. Architecture Diagram (Revised)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Application Code                                   │
│  var orders = await db.Orders.Where(o => o.Status == "Pending").ToListAsync();│
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                              DTDE Core                                       │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                    Shard Resolution (Always Active)                  │   │
│  │  • Analyze query predicates                                          │   │
│  │  • Determine target shards (tables or databases)                     │   │
│  │  • Route queries to appropriate shards                               │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                  Temporal Filtering (Optional)                       │   │
│  │  • Apply ValidAt/ValidBetween predicates                             │   │
│  │  • Handle version creation on updates                                │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                    ┌─────────────────┴─────────────────┐
                    ▼                                   ▼
    ┌───────────────────────────┐       ┌───────────────────────────┐
    │   Table Sharding Mode     │       │  Database Sharding Mode   │
    │  ┌─────┐ ┌─────┐ ┌─────┐ │       │  ┌─────┐ ┌─────┐ ┌─────┐ │
    │  │T_EU │ │T_US │ │T_AP │ │       │  │DB_EU│ │DB_US│ │DB_AP│ │
    │  └─────┘ └─────┘ └─────┘ │       │  └─────┘ └─────┘ └─────┘ │
    │     Same Database         │       │   Different Databases     │
    └───────────────────────────┘       └───────────────────────────┘
```

---

## 9. Key Differences from Original Design

| Aspect | Original | Revised |
|--------|----------|---------|
| Primary Feature | Temporal versioning | Transparent sharding |
| Temporal | Required for DTDE entities | Optional per entity |
| Sharding | Secondary feature | Primary feature |
| ValidFrom/ValidTo | Expected pattern | Any property names |
| Shard Storage | Separate databases only | Tables OR databases |
| Manual Tables | Not supported | Full support (sqlproj) |
| Migrations | Always EF-managed | Optional (can use external) |
| Default Behavior | Version bump on update | Standard EF Core update |

