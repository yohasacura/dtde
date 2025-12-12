# DTDE Development Plan - Sharding Strategies

[← Back to Revised Architecture](00-revised-architecture.md) | [Next: Core Domain Model →](02-core-domain-model.md)

---

## 1. Sharding Strategy Overview

DTDE provides multiple sharding strategies that can be applied to any entity property. All strategies are **property-agnostic** - you choose which property to shard by.

---

## 2. Property-Based Sharding

### 2.1 Single Property Sharding

Shard by any property value:

```csharp
// Shard by Region (string)
entity.ShardBy(c => c.Region);
// Results in: Customers_EU, Customers_US, Customers_APAC

// Shard by Status (enum)
entity.ShardBy(o => o.Status);
// Results in: Orders_Pending, Orders_Completed, Orders_Cancelled

// Shard by Year (int)
entity.ShardBy(o => o.Year);
// Results in: Orders_2023, Orders_2024, Orders_2025
```

### 2.2 Composite Property Sharding

Shard by multiple properties:

```csharp
// Shard by Year + Region
entity.ShardBy(o => new { o.Year, o.Region });
// Results in: Orders_2024_EU, Orders_2024_US, Orders_2025_EU

// Shard by Category + SubCategory
entity.ShardBy(p => new { p.Category, p.SubCategory });
// Results in: Products_Electronics_Phones, Products_Electronics_Laptops
```

---

## 3. Hash-Based Sharding

### 3.1 Even Distribution

Distribute records evenly across a fixed number of shards:

```csharp
// Distribute by ID hash
entity.ShardByHash(c => c.Id, shardCount: 8);
// Results in: Customers_0, Customers_1, ... Customers_7

// Distribute by composite key
entity.ShardByHash(o => new { o.CustomerId, o.OrderId }, shardCount: 16);
```

### 3.2 Custom Hash Function

```csharp
// Custom hash function
entity.ShardByHash(c => c.Id, shardCount: 4, 
    hashFunction: id => Math.Abs(id.GetHashCode()) % 4);

// Consistent hashing (for dynamic shard count)
entity.ShardByConsistentHash(c => c.Id, virtualNodes: 150);
```

---

## 4. Range-Based Sharding

### 4.1 Numeric Ranges

```csharp
// Shard by ID ranges
entity.ShardByRange(c => c.Id, new[]
{
    new ShardRange("low", 0, 999_999),
    new ShardRange("mid", 1_000_000, 9_999_999),
    new ShardRange("high", 10_000_000, int.MaxValue)
});
// Results in: Customers_low, Customers_mid, Customers_high

// Shard by amount ranges
entity.ShardByRange(o => o.Amount, new[]
{
    new ShardRange("small", 0m, 999.99m),
    new ShardRange("medium", 1000m, 9999.99m),
    new ShardRange("large", 10000m, decimal.MaxValue)
});
```

### 4.2 Date Ranges

```csharp
// Shard by explicit date ranges
entity.ShardByRange(o => o.OrderDate, new[]
{
    new ShardRange("archive", DateTime.MinValue, new DateTime(2022, 12, 31)),
    new ShardRange("2023", new DateTime(2023, 1, 1), new DateTime(2023, 12, 31)),
    new ShardRange("2024", new DateTime(2024, 1, 1), new DateTime(2024, 12, 31)),
    new ShardRange("current", new DateTime(2025, 1, 1), DateTime.MaxValue)
});
```

---

## 5. Date-Based Sharding

### 5.1 Automatic Date Intervals

```csharp
// Yearly sharding
entity.ShardByDate(o => o.OrderDate, DateShardInterval.Year);
// Results in: Orders_2023, Orders_2024, Orders_2025

// Monthly sharding
entity.ShardByDate(o => o.OrderDate, DateShardInterval.Month);
// Results in: Orders_2024_01, Orders_2024_02, ... Orders_2024_12

// Quarterly sharding
entity.ShardByDate(o => o.OrderDate, DateShardInterval.Quarter);
// Results in: Orders_2024_Q1, Orders_2024_Q2, Orders_2024_Q3, Orders_2024_Q4

// Weekly sharding
entity.ShardByDate(o => o.OrderDate, DateShardInterval.Week);
// Results in: Orders_2024_W01, Orders_2024_W02, ... Orders_2024_W52
```

### 5.2 Custom Date Formatting

```csharp
// Custom date pattern
entity.ShardByDate(o => o.OrderDate, DateShardInterval.Month, 
    formatPattern: "yyyyMM");
// Results in: Orders_202401, Orders_202402

// Custom shard name generator
entity.ShardByDate(o => o.OrderDate, 
    shardNameGenerator: date => $"Orders_{date:yyyy}_{(date.Month <= 6 ? "H1" : "H2")}");
// Results in: Orders_2024_H1, Orders_2024_H2
```

---

## 6. Alphabetic Sharding

### 6.1 First Letter Sharding

```csharp
// Shard by first letter
entity.ShardByAlphabet(c => c.LastName);
// Results in: Customers_A, Customers_B, ... Customers_Z

// Shard by letter groups
entity.ShardByAlphabet(c => c.LastName, new[] { "A-F", "G-L", "M-R", "S-Z" });
// Results in: Customers_A-F, Customers_G-L, Customers_M-R, Customers_S-Z
```

### 6.2 Custom Character Ranges

```csharp
// Custom ranges with special handling
entity.ShardByAlphabet(c => c.Name, new AlphabetShardConfig
{
    Ranges = new[] { "0-9", "A-M", "N-Z" },
    DefaultShard = "Other", // For special characters
    CaseSensitive = false
});
```

---

## 7. Row Count Sharding

### 7.1 Automatic Shard Rotation

```csharp
// Create new shard when current reaches limit
entity.ShardByRowCount(maxRowsPerShard: 1_000_000);
// Automatically creates: Orders_1, Orders_2, Orders_3...

// With custom naming
entity.ShardByRowCount(maxRowsPerShard: 500_000, 
    shardNameGenerator: index => $"Orders_Partition_{index:D3}");
// Results in: Orders_Partition_001, Orders_Partition_002
```

### 7.2 Row Count with Date Fallback

```csharp
// Combine row count with date for deterministic routing
entity.ShardByRowCount(maxRowsPerShard: 1_000_000,
    fallbackStrategy: ShardByDate(o => o.CreatedAt, DateShardInterval.Month));
```

---

## 8. Expression-Based Sharding

### 8.1 Custom LINQ Expressions

```csharp
// Custom shard key calculation
entity.ShardBy(o => o.Amount > 10000 ? "HighValue" : "Standard");
// Results in: Orders_HighValue, Orders_Standard

// Complex logic
entity.ShardBy(c => 
    c.Country == "US" ? (c.State.StartsWith("C") ? "US_West" : "US_East")
    : c.Country == "UK" ? "EU_UK"
    : "EU_Other");
```

### 8.2 Custom Shard Resolver

```csharp
// Full control with custom resolver
entity.ShardBy(new CustomShardResolver<Order>(order =>
{
    if (order.Priority == Priority.Urgent)
        return "urgent_orders";
    if (order.Amount > 100000)
        return "large_orders";
    return $"orders_{order.CreatedAt.Year}";
}));
```

---

## 9. Manual Sharding (sqlproj Support)

### 9.1 Explicit Table Mapping

For pre-created tables (sqlproj, DacPac, manual SQL):

```csharp
entity.UseManualSharding(config =>
{
    // Explicit table definitions
    config.AddTable("dbo.Orders_2023", o => o.OrderDate.Year == 2023);
    config.AddTable("dbo.Orders_2024", o => o.OrderDate.Year == 2024);
    config.AddTable("dbo.Orders_Current", o => o.OrderDate.Year >= 2025)
          .AsWritable(); // Only writable shard
    config.AddTable("archive.Orders_Historical", o => o.OrderDate.Year < 2023)
          .AsReadOnly();
});
```

### 9.2 Table Discovery

```csharp
// Auto-discover tables at runtime
entity.UseManualSharding(config =>
{
    config.DiscoverTables("Orders_%"); // SQL LIKE pattern
    config.ShardKeyExtractor = tableName => 
    {
        // Extract shard key from table name
        var year = int.Parse(tableName.Split('_').Last());
        return new ShardKeyInfo { Year = year };
    };
});
```

### 9.3 Configuration File

```json
// shards.json
{
  "entities": {
    "Order": {
      "storageMode": "Tables",
      "migrationsEnabled": false,
      "shards": [
        {
          "name": "orders_2023",
          "table": "dbo.Orders_2023",
          "predicate": "OrderDate.Year == 2023",
          "isReadOnly": true
        },
        {
          "name": "orders_2024",
          "table": "dbo.Orders_2024",
          "predicate": "OrderDate.Year == 2024",
          "isWritable": true
        }
      ]
    }
  }
}
```

```csharp
// Load from config
dtde.LoadShardingConfig("shards.json");
```

---

## 10. Combining Strategies

### 10.1 Hierarchical Sharding

```csharp
// First by region (database), then by year (table)
entity.ShardBy(o => o.Region, ShardStorageMode.Databases)
      .ThenShardBy(o => o.Year, ShardStorageMode.Tables);
// Results in: DB_EU.Orders_2024, DB_US.Orders_2024, etc.
```

### 10.2 Strategy with Overflow

```csharp
// Primary strategy with overflow handling
entity.ShardByDate(o => o.OrderDate, DateShardInterval.Year)
      .WithOverflowStrategy(ShardByRowCount(1_000_000));
// If 2024 exceeds 1M rows, creates Orders_2024_1, Orders_2024_2
```

---

## 11. Strategy Interface

```csharp
public interface IShardingStrategy<TEntity> where TEntity : class
{
    /// <summary>
    /// Determines the shard key for an entity.
    /// </summary>
    string GetShardKey(TEntity entity);
    
    /// <summary>
    /// Determines which shards to query based on predicates.
    /// </summary>
    IEnumerable<string> ResolveShards(
        IReadOnlyDictionary<string, object?> predicates,
        ShardRegistry registry);
    
    /// <summary>
    /// Determines which shard to write to for new entities.
    /// </summary>
    string ResolveWriteShard(TEntity entity, ShardRegistry registry);
}
```

---

## 12. Configuration Summary

| Strategy | Best For | Example |
|----------|----------|---------|
| Property | Natural partitions (region, status) | `ShardBy(c => c.Region)` |
| Hash | Even distribution, no hotspots | `ShardByHash(c => c.Id, 8)` |
| Range | Numeric/date ranges | `ShardByRange(c => c.Id, ranges)` |
| Date | Time-series data | `ShardByDate(o => o.Date, Year)` |
| Alphabetic | Name-based lookups | `ShardByAlphabet(c => c.Name)` |
| Row Count | Auto-scaling | `ShardByRowCount(1_000_000)` |
| Expression | Complex logic | `ShardBy(o => CalcShard(o))` |
| Manual | Pre-created tables | `UseManualSharding(...)` |
