# Sharding Guide

This comprehensive guide covers all sharding strategies available in DTDE. Learn how to distribute your data effectively across multiple tables or databases.

## Table of Contents

- [Overview](#overview)
- [Storage Modes](#storage-modes)
- [Sharding Strategies](#sharding-strategies)
  - [Property-Based Sharding](#property-based-sharding)
  - [Hash Sharding](#hash-sharding)
  - [Date Sharding](#date-sharding)
  - [Range Sharding](#range-sharding)
  - [Alphabetic Sharding](#alphabetic-sharding)
- [Shard Configuration](#shard-configuration)
- [Query Optimization](#query-optimization)
- [Best Practices](#best-practices)

---

## Overview

DTDE provides **transparent horizontal sharding** for EF Core entities. Your application code remains unchanged while DTDE handles:

- **Query Routing**: Automatically determines which shard(s) to query
- **Parallel Execution**: Queries multiple shards simultaneously
- **Result Merging**: Combines results into a unified response
- **Write Routing**: Routes inserts/updates to the correct shard

### Key Principle

> Write standard EF Core LINQ queries. DTDE handles the distribution.

```csharp
// You write this (standard EF Core):
var customers = await db.Customers
    .Where(c => c.Region == "EU")
    .OrderBy(c => c.Name)
    .ToListAsync();

// DTDE executes:
// 1. Analyzes query to determine target shard(s)
// 2. Routes to EU shard (optimized)
// 3. Returns results
```

---

## Storage Modes

DTDE supports two storage modes for shards:

### Table Sharding

Multiple tables in the **same database**. Best for:
- Simpler deployment
- Shared infrastructure
- Moderate data volumes

```csharp
entity.ShardBy(c => c.Region)
      .WithStorageMode(ShardStorageMode.Tables);

// Creates tables:
// - Customers_EU
// - Customers_US
// - Customers_APAC
```

### Database Sharding

Separate databases (same or different servers). Best for:
- True horizontal scaling
- Data isolation requirements
- Very large datasets
- Compliance/geographic requirements

```csharp
entity.ShardBy(c => c.Region)
      .WithStorageMode(ShardStorageMode.Databases);

// Uses separate databases:
// - Server_EU/Customers
// - Server_US/Customers
// - Server_APAC/Customers
```

---

## Sharding Strategies

### Property-Based Sharding

Shard by any entity property value. Ideal for categorical data.

#### Basic Property Sharding

```csharp
// Shard by a string property
modelBuilder.Entity<Customer>(entity =>
{
    entity.ShardBy(c => c.Region);
    // Shards: Customers_EU, Customers_US, Customers_APAC
});

// Shard by an enum property
modelBuilder.Entity<Order>(entity =>
{
    entity.ShardBy(o => o.Status);
    // Shards: Orders_Pending, Orders_Processing, Orders_Completed
});
```

#### Composite Property Sharding

```csharp
// Shard by multiple properties
modelBuilder.Entity<SalesRecord>(entity =>
{
    entity.ShardBy(s => new { s.Year, s.Region });
    // Shards: SalesRecords_2024_EU, SalesRecords_2024_US, etc.
});
```

#### Configuration

```csharp
options.UseDtde(dtde =>
{
    dtde.AddShard(s => s
        .WithId("EU")
        .WithShardKeyValue("EU")
        .WithTable("Customers_EU", "dbo"));

    dtde.AddShard(s => s
        .WithId("US")
        .WithShardKeyValue("US")
        .WithTable("Customers_US", "dbo"));
});
```

### Hash Sharding

Distribute data evenly using a hash function. Ideal for:
- Even distribution
- No natural partitioning key
- High-cardinality keys (IDs)

```csharp
// Hash by ID into 8 shards
modelBuilder.Entity<Customer>(entity =>
{
    entity.ShardByHash(c => c.Id, shardCount: 8);
    // Shards: Customers_0, Customers_1, ... Customers_7
});
```

#### How It Works

```
ID: 1234  → Hash: 1234 % 8 = 2 → Customers_2
ID: 5678  → Hash: 5678 % 8 = 6 → Customers_6
ID: 9999  → Hash: 9999 % 8 = 7 → Customers_7
```

#### Configuration

```csharp
options.UseDtde(dtde =>
{
    for (int i = 0; i < 8; i++)
    {
        dtde.AddShard(s => s
            .WithId($"Shard{i}")
            .WithShardKeyValue(i.ToString())
            .WithTable($"Customers_{i}", "dbo"));
    }
});
```

### Date Sharding

Partition data by time periods. Ideal for:
- Time-series data
- Log/event data
- Financial records
- Archival strategies

#### Standard Intervals

```csharp
// Yearly sharding
entity.ShardByDate(o => o.CreatedAt, DateShardInterval.Year);
// Shards: Orders_2023, Orders_2024, Orders_2025

// Monthly sharding
entity.ShardByDate(o => o.CreatedAt, DateShardInterval.Month);
// Shards: Orders_2024_01, Orders_2024_02, ... Orders_2024_12

// Quarterly sharding
entity.ShardByDate(o => o.CreatedAt, DateShardInterval.Quarter);
// Shards: Orders_2024_Q1, Orders_2024_Q2, Orders_2024_Q3, Orders_2024_Q4

// Daily sharding
entity.ShardByDate(o => o.CreatedAt, DateShardInterval.Day);
// Shards: Orders_2024_01_01, Orders_2024_01_02, ...
```

#### Configuration with Date Ranges

```csharp
options.UseDtde(dtde =>
{
    dtde.AddShard(s => s
        .WithId("2023")
        .WithTable("Orders_2023", "dbo")
        .WithDateRange(new DateTime(2023, 1, 1), new DateTime(2023, 12, 31))
        .WithTier(ShardTier.Cold)  // Archived data
        .AsReadOnly());

    dtde.AddShard(s => s
        .WithId("2024")
        .WithTable("Orders_2024", "dbo")
        .WithDateRange(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31))
        .WithTier(ShardTier.Hot));

    dtde.AddShard(s => s
        .WithId("2025")
        .WithTable("Orders_2025", "dbo")
        .WithDateRange(new DateTime(2025, 1, 1), new DateTime(2025, 12, 31))
        .WithTier(ShardTier.Hot));
});
```

### Range Sharding

Partition by numeric or value ranges. Ideal for:
- ID ranges
- Amount/value tiers
- Sequential data

```csharp
// Shard by ID ranges
entity.ShardByRange(c => c.Id, new[]
{
    new ShardRange("legacy", 0, 999_999),
    new ShardRange("current", 1_000_000, 9_999_999),
    new ShardRange("new", 10_000_000, int.MaxValue)
});

// Shard by amount tiers
entity.ShardByRange(o => o.Amount, new[]
{
    new ShardRange("small", 0m, 999.99m),
    new ShardRange("medium", 1000m, 9999.99m),
    new ShardRange("large", 10000m, decimal.MaxValue)
});
```

### Alphabetic Sharding

Partition by first letter(s). Ideal for:
- Name-based lookups
- Directory-style data
- Contact management

```csharp
// Single letter shards
entity.ShardByAlphabet(c => c.LastName);
// Shards: Customers_A, Customers_B, ... Customers_Z

// Grouped letter ranges
entity.ShardByAlphabet(c => c.LastName, new[] { "A-F", "G-L", "M-R", "S-Z" });
// Shards: Customers_A-F, Customers_G-L, Customers_M-R, Customers_S-Z
```

---

## Shard Configuration

### Programmatic Configuration

```csharp
options.UseDtde(dtde =>
{
    dtde.AddShard(s => s
        .WithId("Shard1")
        .WithName("Primary Shard")
        .WithShardKeyValue("EU")
        .WithTable("Customers_EU", "dbo")
        .WithTier(ShardTier.Hot)
        .WithPriority(100));
});
```

### JSON Configuration

Create `shards.json`:

```json
{
  "shards": [
    {
      "shardId": "shard-2024",
      "name": "Year 2024 Data",
      "tableName": "Orders_2024",
      "tier": "Hot",
      "priority": 100,
      "isReadOnly": false,
      "dateRangeStart": "2024-01-01T00:00:00",
      "dateRangeEnd": "2024-12-31T23:59:59"
    },
    {
      "shardId": "shard-2023",
      "name": "Year 2023 Archive",
      "tableName": "Orders_2023",
      "tier": "Cold",
      "priority": 50,
      "isReadOnly": true,
      "dateRangeStart": "2023-01-01T00:00:00",
      "dateRangeEnd": "2023-12-31T23:59:59"
    }
  ]
}
```

Load in configuration:

```csharp
options.UseDtde(dtde =>
{
    dtde.AddShardsFromConfig("shards.json");
});
```

### Shard Tiers

DTDE supports tiered storage for performance optimization:

| Tier | Description | Use Case |
|------|-------------|----------|
| `Hot` | Active data, fast storage | Current records, frequent access |
| `Warm` | Less active data | Recent historical data |
| `Cold` | Archived data, slow storage | Old records, infrequent access |
| `Archive` | Long-term storage | Compliance, audit trails |

```csharp
dtde.AddShard(s => s
    .WithId("archive-2020")
    .WithTable("Orders_2020", "archive")
    .WithTier(ShardTier.Archive)
    .AsReadOnly());  // Prevent writes to archived data
```

---

## Query Optimization

### Shard-Aware Queries

DTDE optimizes queries based on predicates:

```csharp
// ✅ Optimized: Only queries EU shard
var euCustomers = await db.Customers
    .Where(c => c.Region == "EU")
    .ToListAsync();

// ✅ Optimized: Only queries 2024 shard
var recentOrders = await db.Orders
    .Where(o => o.CreatedAt >= new DateTime(2024, 1, 1))
    .ToListAsync();

// ⚠️ Cross-shard: Queries all shards
var allCustomers = await db.Customers.ToListAsync();
```

### Parallel Execution

Configure parallel execution:

```csharp
options.UseDtde(dtde =>
{
    dtde.SetMaxParallelShards(10);  // Max concurrent shard queries
});
```

### Diagnostics

Enable diagnostics to monitor shard queries:

```csharp
options.UseDtde(dtde =>
{
    dtde.EnableDiagnostics();
});
```

---

## Best Practices

### 1. Choose the Right Shard Key

| Data Type | Recommended Strategy |
|-----------|---------------------|
| Categorical (Region, Status) | Property-based |
| High-cardinality ID | Hash |
| Time-series | Date-based |
| Sequential numeric | Range |
| Names/Text | Alphabetic |

### 2. Balance Shard Sizes

Aim for even distribution. Monitor and rebalance if needed.

### 3. Consider Query Patterns

Choose a shard key that aligns with your most common query filters.

### 4. Plan for Growth

- Use date sharding for naturally growing data
- Configure automatic shard creation for row-count based sharding

### 5. Implement Tiered Storage

Move old data to cold/archive tiers for cost optimization.

### 6. Test Cross-Shard Queries

Ensure acceptable performance for queries spanning multiple shards.

---

## Examples

### Multi-Tenant SaaS

```csharp
entity.ShardBy(e => e.TenantId)
      .WithStorageMode(ShardStorageMode.Databases);

// Each tenant gets an isolated database
dtde.AddShard(s => s
    .WithId("tenant-acme")
    .WithShardKeyValue("acme")
    .WithConnectionString("Server=shard1;Database=Tenant_Acme;..."));
```

### Time-Series Data

```csharp
entity.ShardByDate(e => e.Timestamp, DateShardInterval.Month)
      .WithStorageMode(ShardStorageMode.Tables);

// Automatic monthly tables
// Metrics_2024_01, Metrics_2024_02, etc.
```

### Geographic Distribution

```csharp
entity.ShardBy(e => e.Region)
      .WithStorageMode(ShardStorageMode.Databases);

// Regional databases for compliance
dtde.AddShard(s => s
    .WithId("EU")
    .WithConnectionString("Server=eu.db.com;Database=Data;..."));

dtde.AddShard(s => s
    .WithId("US")
    .WithConnectionString("Server=us.db.com;Database=Data;..."));
```

---

## Next Steps

- [Temporal Guide](temporal-guide.md) - Add version tracking
- [Configuration Reference](../wiki/configuration.md) - All options
- [API Reference](../wiki/api-reference.md) - Complete API docs

---

[← Back to Guides](index.md) | [Next: Temporal Guide →](temporal-guide.md)
