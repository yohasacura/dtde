# Temporal Versioning Guide

This guide covers DTDE's optional temporal versioning feature, which enables point-in-time queries and version tracking for your entities.

## Table of Contents

- [Overview](#overview)
- [When to Use Temporal](#when-to-use-temporal)
- [Configuration](#configuration)
- [Querying Temporal Data](#querying-temporal-data)
- [Write Operations](#write-operations)
- [Versioning Modes](#versioning-modes)
- [Relationships](#relationships)
- [Best Practices](#best-practices)

---

## Overview

Temporal versioning allows you to:

- **Track entity history** over time
- **Query data as it existed** at any point in time
- **Maintain audit trails** automatically
- **Support bi-temporal data** patterns

### Key Principle

> Temporal versioning is **completely optional**. Entities can use sharding alone, temporal alone, or both together.

---

## When to Use Temporal

### ✅ Good Use Cases

| Scenario | Why Temporal Helps |
|----------|-------------------|
| **Contracts/Agreements** | Track amendments and historical terms |
| **Pricing/Rates** | Query prices as they were at order time |
| **Policies/Rules** | Apply rules valid at transaction time |
| **Audit Requirements** | Regulatory compliance, change history |
| **Insurance/Finance** | Point-in-time calculations |
| **HR/Employee Records** | Historical salary, position tracking |

### ❌ When to Avoid

- High-frequency updates (100+ per second per entity)
- Large entities with small changes
- Simple CRUD without history requirements

---

## Configuration

### Entity Setup

Add temporal validity properties to your entity. **Use any property names** - DTDE is property-agnostic:

```csharp
public class Contract
{
    public int Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CustomerName { get; set; } = string.Empty;

    // Temporal properties - choose your own names
    public DateTime EffectiveDate { get; set; }      // When this version becomes valid
    public DateTime? ExpirationDate { get; set; }    // When this version expires (null = current)
}
```

### DbContext Configuration

Configure temporal validity in `OnModelCreating`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    modelBuilder.Entity<Contract>(entity =>
    {
        entity.HasKey(c => c.Id);

        // Configure sharding (optional)
        entity.ShardByDate(c => c.EffectiveDate, DateInterval.Year);

        // Configure temporal validity
        entity.HasTemporalValidity(
            validFrom: c => c.EffectiveDate,
            validTo: c => c.ExpirationDate);
    });
}
```

### String-Based Configuration

For scenarios where property names come from configuration:

```csharp
entity.HasTemporalValidity(
    validFromProperty: "EffectiveDate",
    validToProperty: "ExpirationDate");
```

### Nullable vs Non-Nullable End Date

```csharp
// Nullable (recommended) - null means currently valid
public DateTime? ExpirationDate { get; set; }

// Non-nullable - use max value for current
public DateTime ExpirationDate { get; set; }

// Configure max value behavior:
entity.HasTemporalValidity(c => c.ValidFrom, c => c.ValidTo)
      .WithOpenEndedValue(DateTime.MaxValue);
```

---

## Querying Temporal Data

### Point-in-Time Queries

Query data as it existed at a specific moment:

```csharp
// Get contracts valid today
var currentContracts = await _context.ValidAt<Contract>(DateTime.Today)
    .ToListAsync();

// Get contracts valid at a specific date
var historicalContracts = await _context.ValidAt<Contract>(new DateTime(2023, 6, 15))
    .ToListAsync();

// Combine with other filters
var activeEuContracts = await _context.ValidAt<Contract>(DateTime.Today)
    .Where(c => c.Region == "EU" && c.Amount > 10000)
    .OrderBy(c => c.CustomerName)
    .ToListAsync();
```

### Range Queries

Query entities valid within a time range:

```csharp
// Get contracts valid during Q1 2024
var q1Contracts = await _context.ValidBetween<Contract>(
    new DateTime(2024, 1, 1),
    new DateTime(2024, 3, 31))
    .ToListAsync();

// An entity is included if it was valid at ANY point in the range
// (ValidFrom <= endDate AND (ValidTo IS NULL OR ValidTo >= startDate))
```

### All Versions Query

Bypass temporal filtering to see full history:

```csharp
// Get all versions of all contracts
var allVersions = await _context.AllVersions<Contract>()
    .OrderBy(c => c.EffectiveDate)
    .ToListAsync();

// Get all versions of a specific contract
var contractHistory = await _context.AllVersions<Contract>()
    .Where(c => c.ContractNumber == "CTR-001")
    .OrderBy(c => c.EffectiveDate)
    .ToListAsync();
```

### Non-Temporal Entity Behavior

For entities **without** `HasTemporalValidity()`, temporal methods return all records:

```csharp
// If Customer doesn't have temporal configuration:
var customers = await _context.ValidAt<Customer>(DateTime.Today).ToListAsync();
// Equivalent to: await _context.Customers.ToListAsync();
```

---

## Write Operations

### Without Temporal (Default)

Standard EF Core behavior - DTDE only routes to the correct shard:

```csharp
// UPDATE - overwrites in place
var order = await _context.Orders.FindAsync(id);
order.Status = "Completed";
await _context.SaveChangesAsync();
// SQL: UPDATE Orders_2024 SET Status = 'Completed' WHERE Id = @id

// DELETE - removes record
_context.Orders.Remove(order);
await _context.SaveChangesAsync();
// SQL: DELETE FROM Orders_2024 WHERE Id = @id
```

### With Temporal (Version Bump)

When temporal is configured, updates create new versions:

```csharp
// UPDATE - creates new version
var contract = await _context.ValidAt<Contract>(DateTime.Today).FirstAsync();
contract.Amount = 50000;
await _context.SaveChangesAsync();

// SQL (version bump):
// 1. UPDATE Contracts SET ExpirationDate = @now WHERE Id = @oldId
// 2. INSERT INTO Contracts (..., EffectiveDate, ExpirationDate) VALUES (..., @now, NULL)
```

### Explicit Temporal Operations

Use explicit methods for fine-grained control:

```csharp
// Add with specific effective date
_context.AddTemporal(contract, effectiveFrom: new DateTime(2024, 7, 1));
await _context.SaveChangesAsync();

// Create new version with explicit dates
var newVersion = _context.CreateNewVersion(contract, changes =>
{
    changes.Amount = 75000;
    changes.CustomerName = "Updated Corp";
}, effectiveFrom: new DateTime(2024, 8, 1));
await _context.SaveChangesAsync();

// Terminate (close validity)
_context.Terminate(contract, terminationDate: new DateTime(2024, 12, 31));
await _context.SaveChangesAsync();
```

---

## Versioning Modes

### Soft Versioning (Default)

Old record gets `ValidTo` set, new record created:

```csharp
entity.HasTemporalValidity(e => e.ValidFrom, e => e.ValidTo)
      .WithVersioningMode(VersioningMode.SoftVersion);
```

**On update:**
1. `UPDATE` old record: `SET ValidTo = @now`
2. `INSERT` new record: `ValidFrom = @now, ValidTo = NULL`

**Data:**
```
| Id | Amount | ValidFrom  | ValidTo    |
|----|--------|------------|------------|
| 1  | 10000  | 2024-01-01 | 2024-06-30 |  ← Old version
| 2  | 15000  | 2024-07-01 | NULL       |  ← Current version
```

### Audit Trail

Old record copied to history table, current record updated:

```csharp
entity.HasTemporalValidity(e => e.ValidFrom, e => e.ValidTo)
      .WithVersioningMode(VersioningMode.AuditTrail)
      .WithHistoryTable("ContractHistory");
```

**On update:**
1. `INSERT INTO ContractHistory SELECT * FROM Contracts WHERE Id = @id`
2. `UPDATE Contracts SET Amount = @new, ValidFrom = @now WHERE Id = @id`

### Append Only

Never update, always insert new versions. Ideal for immutable audit logs:

```csharp
entity.HasTemporalValidity(e => e.ValidFrom, e => e.ValidTo)
      .WithVersioningMode(VersioningMode.AppendOnly);
```

**On update:**
- `INSERT` new record only
- Old records are never modified

---

## Relationships

### Temporal Parent with Temporal Children

Configure temporal validity on related entities:

```csharp
public class Contract
{
    public int Id { get; set; }
    public DateTime EffectiveDate { get; set; }
    public DateTime? ExpirationDate { get; set; }

    public List<ContractLine> Lines { get; set; } = new();
}

public class ContractLine
{
    public int Id { get; set; }
    public int ContractId { get; set; }

    // Child has its own validity (must be within parent)
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public Contract Contract { get; set; } = null!;
}
```

```csharp
modelBuilder.Entity<Contract>(entity =>
{
    entity.HasTemporalValidity(c => c.EffectiveDate, c => c.ExpirationDate);

    entity.HasMany(c => c.Lines)
        .WithOne(l => l.Contract)
        .HasForeignKey(l => l.ContractId);
});

modelBuilder.Entity<ContractLine>(entity =>
{
    entity.HasTemporalValidity(l => l.StartDate, l => l.EndDate);
});
```

### Temporal Include

Load related entities with temporal filtering:

```csharp
// Get contract with lines valid at the same point in time
var contract = await _context.ValidAt<Contract>(DateTime.Today)
    .Include(c => c.Lines) // Also filters lines to valid at DateTime.Today
    .FirstAsync(c => c.Id == contractId);
```

---

## Best Practices

### 1. Choose Appropriate Property Names

Use domain-specific names that make sense for your business:

| Domain | Start Property | End Property |
|--------|---------------|--------------|
| Contracts | EffectiveDate | ExpirationDate |
| Insurance | PolicyStart | PolicyEnd |
| Employment | StartDate | EndDate |
| Pricing | ValidFrom | ValidTo |

### 2. Use Nullable End Dates

Prefer nullable `DateTime?` for end dates to clearly indicate current validity:

```csharp
public DateTime? ExpirationDate { get; set; }  // null = currently valid
```

### 3. Index Temporal Columns

Create indexes for efficient temporal queries:

```sql
CREATE INDEX IX_Contract_Temporal
ON Contracts (EffectiveDate, ExpirationDate)
INCLUDE (ContractNumber, Amount);
```

### 4. Consider Query Patterns

- If you mostly query current data, `ValidAt(DateTime.Today)` is common
- If you need history access, use `AllVersions()` with appropriate filters

### 5. Plan for Data Growth

Temporal versioning increases data volume. Combine with:
- Date-based sharding to manage growth
- Tiered storage for old versions
- Archive strategies for historical data

### 6. Handle Version Conflicts

In concurrent environments, consider:
- Optimistic concurrency with row versions
- Logical timestamps for ordering
- Business rules for overlapping validity

---

## Advanced Scenarios

### Bi-Temporal Data

Track both **valid time** and **transaction time**:

```csharp
public class BiTemporalEntity
{
    // Valid time (business time)
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    // Transaction time (system time)
    public DateTime TransactionFrom { get; set; }
    public DateTime? TransactionTo { get; set; }
}
```

### Temporal Aggregations

Calculate aggregates over time:

```csharp
// Total contract value as of end of each quarter
var quarterlyTotals = await _context.AllVersions<Contract>()
    .GroupBy(c => new { Year = c.EffectiveDate.Year, Quarter = (c.EffectiveDate.Month - 1) / 3 + 1 })
    .Select(g => new
    {
        g.Key.Year,
        g.Key.Quarter,
        TotalValue = g.Sum(c => c.Amount)
    })
    .ToListAsync();
```

---

## Next Steps

- [Configuration Reference](../wiki/configuration.md) - Detailed configuration options
- [API Reference](../wiki/api-reference.md) - Complete API documentation
- [Troubleshooting](../wiki/troubleshooting.md) - Common issues and solutions

---

[← Back to Guides](README.md) | [Back to Sharding Guide](sharding-guide.md)
