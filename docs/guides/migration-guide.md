# Migration Guide

This guide helps you migrate an existing Entity Framework Core application to use DTDE for sharding and optional temporal versioning.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Migration Steps](#migration-steps)
- [Database Migration](#database-migration)
- [Code Changes](#code-changes)
- [Testing Your Migration](#testing-your-migration)
- [Rollback Strategy](#rollback-strategy)

---

## Prerequisites

Before migrating:

- [ ] .NET 9.0 SDK or later
- [ ] Existing EF Core application
- [ ] Understanding of your data access patterns
- [ ] Backup of your production database

---

## Migration Steps

### Step 1: Install DTDE Package

```bash
dotnet add package Dtde.EntityFramework
```

### Step 2: Update DbContext Base Class

Change from `DbContext` to `DtdeDbContext`:

```csharp
// Before
public class AppDbContext : DbContext

// After
public class AppDbContext : DtdeDbContext
```

### Step 3: Configure DTDE Services

Update your service registration:

```csharp
// Before
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

// After
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
    options.UseDtde();  // Add DTDE support
});
```

### Step 4: Identify Sharding Candidates

Review your entities and identify candidates for sharding:

| Entity | Good Shard Key | Strategy |
|--------|---------------|----------|
| Orders | CreatedAt, Year | Date-based |
| Customers | Region | Property-based |
| Products | Category | Property-based |
| Logs | Timestamp | Date-based |
| Users | TenantId | Property-based |

### Step 5: Configure Entity Sharding

Add sharding configuration to `OnModelCreating`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    // Configure sharding for specific entities
    modelBuilder.Entity<Order>(entity =>
    {
        entity.ShardByDate(o => o.CreatedAt, DateShardInterval.Year);
    });

    // Non-sharded entities work as normal EF Core
    modelBuilder.Entity<User>(entity =>
    {
        entity.HasKey(u => u.Id);
        // No sharding - works like standard EF Core
    });
}
```

---

## Database Migration

### Option A: New Tables (Recommended for New Data)

1. Create new sharded tables:

```sql
-- Create sharded tables for Orders
CREATE TABLE Orders_2024 (
    Id INT IDENTITY PRIMARY KEY,
    CustomerId INT NOT NULL,
    Amount DECIMAL(18,2),
    CreatedAt DATETIME2,
    -- ... other columns
);

CREATE TABLE Orders_2025 (
    Id INT IDENTITY PRIMARY KEY,
    CustomerId INT NOT NULL,
    Amount DECIMAL(18,2),
    CreatedAt DATETIME2,
    -- ... other columns
);
```

2. Configure DTDE to use these tables:

```csharp
options.UseDtde(dtde =>
{
    dtde.AddShard(s => s
        .WithId("2024")
        .WithTable("Orders_2024", "dbo")
        .WithDateRange(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31)));

    dtde.AddShard(s => s
        .WithId("2025")
        .WithTable("Orders_2025", "dbo")
        .WithDateRange(new DateTime(2025, 1, 1), new DateTime(2025, 12, 31)));
});
```

### Option B: Migrate Existing Data

1. Create sharded tables
2. Migrate data:

```sql
-- Migrate 2024 data
INSERT INTO Orders_2024
SELECT * FROM Orders
WHERE YEAR(CreatedAt) = 2024;

-- Migrate 2025 data
INSERT INTO Orders_2025
SELECT * FROM Orders
WHERE YEAR(CreatedAt) = 2025;
```

3. Verify data integrity
4. Archive or drop original table

### Option C: Keep Original Table as Default Shard

Use existing table as a "catch-all" shard:

```csharp
dtde.AddShard(s => s
    .WithId("default")
    .WithTable("Orders", "dbo")
    .WithPriority(0));  // Lowest priority, used as fallback
```

---

## Code Changes

### Repository Pattern (If Used)

Your repositories require minimal changes:

```csharp
// Before and After - no changes needed!
public class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _context;

    public async Task<List<Order>> GetRecentOrdersAsync()
    {
        // This query works the same way
        // DTDE transparently handles sharding
        return await _context.Orders
            .Where(o => o.CreatedAt >= DateTime.Today.AddDays(-30))
            .ToListAsync();
    }
}
```

### Direct DbContext Usage

Same behavior - no changes needed:

```csharp
// Works exactly the same
var orders = await _context.Orders
    .Where(o => o.CustomerId == customerId)
    .ToListAsync();
```

### Adding Temporal Queries (Optional)

If adding temporal versioning:

```csharp
// New capability: point-in-time queries
var historicalOrders = await _context.ValidAt<Order>(lastYearDate)
    .ToListAsync();
```

---

## Testing Your Migration

### 1. Unit Tests

Existing unit tests should continue to work with mock contexts.

### 2. Integration Tests

Test against sharded databases:

```csharp
[Fact]
public async Task Query_CrossShard_ReturnsAllResults()
{
    // Insert into multiple shards
    _context.Orders.Add(new Order { CreatedAt = new DateTime(2024, 6, 1) });
    _context.Orders.Add(new Order { CreatedAt = new DateTime(2025, 1, 15) });
    await _context.SaveChangesAsync();

    // Query should return both
    var allOrders = await _context.Orders.ToListAsync();
    Assert.Equal(2, allOrders.Count);
}

[Fact]
public async Task Query_SingleShard_OnlyQueriesTargetShard()
{
    // This should only hit 2024 shard
    var orders2024 = await _context.Orders
        .Where(o => o.CreatedAt.Year == 2024)
        .ToListAsync();
}
```

### 3. Performance Testing

Compare query performance before and after:

```csharp
[Fact]
public async Task Performance_ShardedQuery_FastEnough()
{
    var sw = Stopwatch.StartNew();

    var results = await _context.Orders
        .Where(o => o.CreatedAt >= DateTime.Today.AddYears(-1))
        .ToListAsync();

    sw.Stop();
    Assert.True(sw.ElapsedMilliseconds < 200, "Query too slow");
}
```

---

## Rollback Strategy

### If Migration Fails

1. **Revert DbContext**: Change back to `DbContext` base class
2. **Remove DTDE configuration**: Remove `UseDtde()` call
3. **Keep data tables**: Sharded tables remain for later retry

### Gradual Rollout

Enable DTDE per-entity:

```csharp
// Phase 1: Only Orders sharded
modelBuilder.Entity<Order>().ShardByDate(o => o.CreatedAt, DateShardInterval.Year);

// Phase 2: Add Customers
modelBuilder.Entity<Customer>().ShardBy(c => c.Region);

// Unsharded entities work as normal EF Core
```

---

## Checklist

- [ ] Package installed
- [ ] DbContext updated to inherit from `DtdeDbContext`
- [ ] `UseDtde()` added to service configuration
- [ ] Sharding strategies configured
- [ ] Shard definitions added
- [ ] Database tables created
- [ ] Data migrated (if applicable)
- [ ] Unit tests passing
- [ ] Integration tests passing
- [ ] Performance validated

---

## Next Steps

- [Getting Started Guide](getting-started.md) - Full setup walkthrough
- [Sharding Guide](sharding-guide.md) - Detailed sharding options
- [Troubleshooting](../wiki/troubleshooting.md) - Common issues

---

[← Back to Guides](index.md)
