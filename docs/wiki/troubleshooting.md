# Troubleshooting Guide

Common issues, solutions, and frequently asked questions for DTDE.

## Table of Contents

- [Common Issues](#common-issues)
- [Error Messages](#error-messages)
- [Performance Issues](#performance-issues)
- [Configuration Problems](#configuration-problems)
- [FAQ](#faq)
- [Getting Help](#getting-help)

---

## Common Issues

### Empty Query Results

**Symptom:** Queries return no results when data exists.

**Possible Causes:**

1. **Shards not configured**
   ```csharp
   // Check shard count
   var shardCount = context.ShardRegistry.GetAllShards().Count;
   Console.WriteLine($"Configured shards: {shardCount}");
   ```

2. **Shard key mismatch**
   ```csharp
   // Verify shard key values match data
   var shards = context.ShardRegistry.GetAllShards();
   foreach (var shard in shards)
   {
       Console.WriteLine($"Shard: {shard.ShardId}, Key: {shard.ShardKeyValue}");
   }
   ```

3. **Case sensitivity**
   - Default: Case-sensitive matching
   - Ensure shard key values match exactly

**Solution:**
```csharp
// Ensure shard configuration matches data
dtde.AddShard(s => s
    .WithId("EU")
    .WithShardKeyValue("EU")); // Must match data exactly

// If data has "eu" (lowercase), configure accordingly:
dtde.AddShard(s => s
    .WithId("EU")
    .WithShardKeyValue("eu"));
```

### "No Shards Found" Error

**Symptom:** `No shards found for entity type 'X'`

**Causes:**
1. Entity not configured for sharding
2. Shard registry empty
3. Predicate doesn't match any shard

**Solutions:**

1. **Add sharding configuration:**
   ```csharp
   modelBuilder.Entity<Customer>(entity =>
   {
       entity.ShardBy(c => c.Region);
   });
   ```

2. **Add shard definitions:**
   ```csharp
   dtde.AddShard(s => s.WithId("default").WithShardKeyValue("*"));
   ```

3. **Enable diagnostics to debug:**
   ```csharp
   dtde.EnableDiagnostics();
   ```

### Temporal Query Issues

**Symptom:** `ValidAt()` returns all records or no records.

**Causes:**
1. Entity not configured for temporal
2. Date property values incorrect
3. Null handling issue

**Solutions:**

1. **Ensure temporal configuration:**
   ```csharp
   entity.HasTemporalValidity(
       e => e.EffectiveDate,
       e => e.ExpirationDate);
   ```

2. **Check date values:**
   ```csharp
   // Debug: Show actual dates
   var all = await context.AllVersions<Contract>()
       .Select(c => new { c.Id, c.EffectiveDate, c.ExpirationDate })
       .ToListAsync();
   ```

3. **Handle null end dates:**
   - `null` ExpirationDate = currently valid
   - Ensure this matches your data

### SaveChanges Fails

**Symptom:** `SaveChangesAsync()` throws exception.

**Common Causes:**

1. **Write to read-only shard:**
   ```
   Error: Cannot write to read-only shard 'archive-2020'
   ```

   **Solution:** Check shard is writable:
   ```csharp
   var shard = context.ShardRegistry.GetShard("archive-2020");
   if (shard.IsReadOnly)
   {
       // Route to different shard or reject
   }
   ```

2. **Shard key changed:**
   - Changing shard key value on update requires special handling
   - Entity would need to move between shards

3. **Connection issues:**
   - Verify connection strings for database sharding
   - Check network connectivity

---

## Error Messages

### "Shard key property 'X' not found"

```
InvalidOperationException: Shard key property 'Region' not found on entity type 'Customer'.
```

**Cause:** Property name in `ShardBy()` doesn't exist.

**Solution:**
```csharp
// Ensure property exists and spelling is correct
entity.ShardBy(c => c.Region); // Property must exist on Customer
```

### "Multiple shards found for write operation"

```
InvalidOperationException: Multiple shards match for write operation. Cannot determine target shard.
```

**Cause:** Shard configuration overlaps.

**Solution:**
```csharp
// Ensure shard key values don't overlap
dtde.AddShard(s => s.WithShardKeyValue("EU")); // Unique
dtde.AddShard(s => s.WithShardKeyValue("US")); // Unique
```

### "Shard configuration file not found"

```
FileNotFoundException: Shard configuration file not found: shards.json
```

**Solution:**
```csharp
// Use absolute path or ensure file is in correct location
var path = Path.Combine(AppContext.BaseDirectory, "shards.json");
dtde.AddShardsFromConfig(path);
```

### "Cannot create context for shard"

```
InvalidOperationException: Cannot create context for shard 'shard-eu'. Connection string missing.
```

**Cause:** Database shard missing connection string.

**Solution:**
```csharp
dtde.AddShard(s => s
    .WithId("shard-eu")
    .WithConnectionString("Server=...;Database=...")); // Required for database sharding
```

---

## Performance Issues

### Slow Cross-Shard Queries

**Symptom:** Queries spanning multiple shards are slow.

**Diagnosis:**
```csharp
dtde.EnableDiagnostics();
// Check logs for shard execution times
```

**Solutions:**

1. **Optimize shard predicates:**
   ```csharp
   // ✅ Good: Hits single shard
   var euCustomers = await db.Customers
       .Where(c => c.Region == "EU")
       .ToListAsync();

   // ⚠️ Slow: Hits all shards
   var allCustomers = await db.Customers
       .Where(c => c.Name.Contains("Smith"))
       .ToListAsync();
   ```

2. **Increase parallelism:**
   ```csharp
   dtde.SetMaxParallelShards(20); // Default is 10
   ```

3. **Add indexes:**
   ```sql
   CREATE INDEX IX_Customer_Region ON Customers_EU (Region);
   CREATE INDEX IX_Customer_Region ON Customers_US (Region);
   ```

### High Memory Usage

**Symptom:** Memory grows during large queries.

**Solutions:**

1. **Use streaming:**
   ```csharp
   await foreach (var customer in db.Customers.AsAsyncEnumerable())
   {
       // Process one at a time
   }
   ```

2. **Implement pagination:**
   ```csharp
   var page = await db.Customers
       .Skip(pageNumber * pageSize)
       .Take(pageSize)
       .ToListAsync();
   ```

3. **Use projections:**
   ```csharp
   var names = await db.Customers
       .Select(c => new { c.Id, c.Name })
       .ToListAsync();
   ```

### Connection Pool Exhaustion

**Symptom:** `TimeoutException` on high load.

**Solutions:**

1. **Increase pool size:**
   ```csharp
   var connectionString = "Server=...;Max Pool Size=200;...";
   ```

2. **Reduce parallel shards:**
   ```csharp
   dtde.SetMaxParallelShards(5); // Reduce concurrency
   ```

3. **Use connection pooling per shard:**
   ```csharp
   dtde.AddShard(s => s
       .WithConnectionString("...;Max Pool Size=50;..."));
   ```

---

## Configuration Problems

### DbContext Not Using DTDE

**Symptom:** Standard EF Core behavior, no sharding.

**Checklist:**
- [ ] Inherits from `DtdeDbContext`
- [ ] `UseDtde()` called in options
- [ ] Entity has sharding configuration

```csharp
// ✅ Correct
public class AppDbContext : DtdeDbContext { }

// ❌ Wrong
public class AppDbContext : DbContext { }
```

### Shards Not Loading from JSON

**Symptom:** `AddShardsFromConfig()` has no effect.

**Solutions:**

1. **Check file path:**
   ```csharp
   var path = Path.Combine(AppContext.BaseDirectory, "shards.json");
   if (!File.Exists(path))
   {
       throw new FileNotFoundException($"Config not found: {path}");
   }
   dtde.AddShardsFromConfig(path);
   ```

2. **Validate JSON format:**
   ```json
   {
     "shards": [
       {
         "shardId": "required",
         "tableName": "required for tables"
       }
     ]
   }
   ```

3. **Check for JSON errors:**
   ```csharp
   try
   {
       dtde.AddShardsFromConfig(path);
   }
   catch (JsonException ex)
   {
       Console.WriteLine($"JSON Error: {ex.Message}");
   }
   ```

### Entity Configuration Not Applied

**Symptom:** Sharding/temporal not working for specific entity.

**Checklist:**
- [ ] `base.OnModelCreating(modelBuilder)` called first
- [ ] Configuration in correct entity block

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder); // ✅ Must call first

    modelBuilder.Entity<Customer>(entity =>
    {
        entity.ShardBy(c => c.Region); // ✅ Inside Entity<> block
    });
}
```

---

## FAQ

### Can I use DTDE with existing databases?

**Yes.** DTDE works with pre-existing tables:

```csharp
dtde.AddShard(s => s
    .WithId("existing")
    .WithTable("ExistingCustomers", "dbo"));
```

### Can entities use only temporal without sharding?

**Yes.** Configure temporal only:

```csharp
modelBuilder.Entity<Contract>(entity =>
{
    // No ShardBy() - uses single table
    entity.HasTemporalValidity(c => c.ValidFrom, c => c.ValidTo);
});
```

### How do I handle shard key changes?

Shard key changes require special handling:

```csharp
// Option 1: Delete from old shard, insert to new
var customer = await db.Customers.FindAsync(id);
db.Customers.Remove(customer);
await db.SaveChangesAsync();

customer.Region = "EU"; // New shard key
db.Customers.Add(customer);
await db.SaveChangesAsync();

// Option 2: Implement move logic in application
```

### Does DTDE support transactions across shards?

**Not in v1.** Cross-shard transactions are a non-goal for the initial release.

For consistency across shards:
- Use eventual consistency patterns
- Implement saga/compensation patterns
- Use distributed transaction coordinators (external)

### Can I mix sharded and non-sharded entities?

**Yes.** Only configured entities use DTDE features:

```csharp
// Sharded
modelBuilder.Entity<Order>().ShardBy(o => o.Year);

// Non-sharded (standard EF Core)
modelBuilder.Entity<Setting>(); // No ShardBy()
```

### How do I unit test with DTDE?

Use test mode or mock the context:

```csharp
// Option 1: Enable test mode
options.UseDtde(dtde => dtde.EnableTestMode());

// Option 2: Use in-memory database for tests
options.UseInMemoryDatabase("TestDb");
options.UseDtde();
```

### What databases are supported?

**Currently:** SQL Server only (v1).

Future versions may support:
- PostgreSQL
- MySQL
- Azure Cosmos DB

---

## Getting Help

### Enable Diagnostics

```csharp
options.UseDtde(dtde =>
{
    dtde.EnableDiagnostics();
});
```

### Collect Debug Information

```csharp
// Log shard information
var shards = context.ShardRegistry.GetAllShards();
foreach (var shard in shards)
{
    logger.LogDebug("Shard: {Id}, Key: {Key}, Table: {Table}",
        shard.ShardId, shard.ShardKeyValue, shard.TableName);
}

// Log entity metadata
var metadata = context.MetadataRegistry.GetEntityMetadata<Customer>();
logger.LogDebug("Entity: {Type}, HasSharding: {Sharding}, HasTemporal: {Temporal}",
    metadata?.EntityType.Name,
    metadata?.ShardingConfiguration != null,
    metadata?.ValidityConfiguration != null);
```

### Report Issues

When reporting issues, include:

1. **DTDE version**
2. **.NET version**
3. **EF Core version**
4. **Configuration** (sanitized)
5. **Error message** (full stack trace)
6. **Minimal reproduction steps**

---

## Next Steps

- [API Reference](api-reference.md) - Complete API documentation
- [Configuration](configuration.md) - Configuration options
- [Architecture](architecture.md) - System design

---

[← Back to Wiki](README.md) | [Back to API Reference](api-reference.md)
