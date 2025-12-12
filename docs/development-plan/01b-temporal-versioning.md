# DTDE Development Plan - Temporal Versioning (Optional)

[← Back to Sharding Strategies](01a-sharding-strategies.md) | [Next: Core Domain Model →](02-core-domain-model.md)

---

## 1. Overview

Temporal versioning is an **optional feature** in DTDE. Entities can use:
- **Sharding only** (default) - standard EF Core update behavior
- **Sharding + Temporal** - version tracking with configurable properties

---

## 2. Property-Agnostic Temporal Configuration

### 2.1 No Hardcoded Property Names

DTDE does NOT require `ValidFrom`/`ValidTo`. Use ANY property names:

```csharp
// Standard naming
entity.HasTemporalValidity(e => e.ValidFrom, e => e.ValidTo);

// Domain-specific naming
entity.HasTemporalValidity(e => e.EffectiveDate, e => e.ExpirationDate);

// Policy-style naming
entity.HasTemporalValidity(e => e.PolicyStart, e => e.PolicyEnd);

// Single-ended (no end date)
entity.HasTemporalValidity(e => e.CreatedAt); // Open-ended validity
```

### 2.2 String-Based Configuration

```csharp
// Using property names as strings
entity.HasTemporalValidity(
    validFromProperty: "EffectiveDate",
    validToProperty: "ExpirationDate");

// From configuration
entity.HasTemporalValidity(
    validFromProperty: config["TemporalStartProperty"],
    validToProperty: config["TemporalEndProperty"]);
```

### 2.3 Nullable End Date Support

```csharp
// Non-nullable end date
public DateTime ValidTo { get; set; }
entity.HasTemporalValidity(e => e.ValidFrom, e => e.ValidTo)
      .WithOpenEndedValue(DateTime.MaxValue);

// Nullable end date (preferred)
public DateTime? ValidTo { get; set; }
entity.HasTemporalValidity(e => e.ValidFrom, e => e.ValidTo);
// null = currently valid (open-ended)
```

---

## 3. Temporal Query Methods

### 3.1 Core Query Methods

```csharp
// Get entities valid at a specific point in time
var current = await db.Contracts
    .ValidAt(DateTime.Today)
    .ToListAsync();

// Get entities valid within a date range
var q1Contracts = await db.Contracts
    .ValidBetween(new DateTime(2024, 1, 1), new DateTime(2024, 3, 31))
    .ToListAsync();

// Get all versions (bypass temporal filtering)
var history = await db.Contracts
    .AllVersions()
    .Where(c => c.ContractNumber == "CTR-001")
    .OrderBy(c => c.EffectiveDate)
    .ToListAsync();
```

### 3.2 Non-Temporal Entity Behavior

For entities **without** `HasTemporalValidity()`:

```csharp
// ValidAt returns all records (no filtering)
var customers = await db.Customers.ValidAt(DateTime.Today).ToListAsync();
// Equivalent to: await db.Customers.ToListAsync();

// AllVersions returns all records
var allCustomers = await db.Customers.AllVersions().ToListAsync();
// Equivalent to: await db.Customers.ToListAsync();
```

---

## 4. Temporal Write Behavior

### 4.1 Without Temporal (Default)

Standard EF Core behavior - DTDE only routes to correct shard:

```csharp
// UPDATE - overwrites in place
var order = await db.Orders.FindAsync(id);
order.Status = "Completed";
await db.SaveChangesAsync();
// SQL: UPDATE Orders_2024 SET Status = 'Completed' WHERE Id = @id

// DELETE - removes record
db.Orders.Remove(order);
await db.SaveChangesAsync();
// SQL: DELETE FROM Orders_2024 WHERE Id = @id
```

### 4.2 With Temporal Versioning

Version-bump semantics when configured:

```csharp
// UPDATE - creates new version
var contract = await db.Contracts.ValidAt(DateTime.Today).FirstAsync();
contract.Amount = 50000;
await db.SaveChangesAsync();

// SQL (version bump):
// UPDATE Contracts_2024 SET ExpirationDate = @now WHERE Id = @id
// INSERT INTO Contracts_2024 (Id, Amount, EffectiveDate, ExpirationDate) 
//   VALUES (@newId, 50000, @now, NULL)
```

### 4.3 Explicit Temporal Operations

```csharp
// Add with specific effective date
db.AddTemporal(contract, effectiveFrom: new DateTime(2024, 7, 1));

// Create new version with explicit dates
var newVersion = db.CreateNewVersion(contract, changes, effectiveFrom);

// Terminate (close validity)
db.Terminate(contract, terminationDate: DateTime.Today);
```

---

## 5. Versioning Modes

### 5.1 No Versioning (Default)

```csharp
// Default - no version tracking
entity.ShardBy(o => o.Year);
// Updates overwrite, deletes remove
```

### 5.2 Soft Versioning

```csharp
// Old record gets ValidTo set, new record created
entity.HasTemporalValidity(e => e.ValidFrom, e => e.ValidTo)
      .WithVersioningMode(VersioningMode.SoftVersion);

// On update:
// 1. UPDATE old: SET ValidTo = @now
// 2. INSERT new: ValidFrom = @now, ValidTo = NULL
```

### 5.3 Audit Trail

```csharp
// Old record copied to history table, current record updated
entity.HasTemporalValidity(e => e.ValidFrom, e => e.ValidTo)
      .WithVersioningMode(VersioningMode.AuditTrail)
      .WithHistoryTable("ContractHistory");

// On update:
// 1. INSERT INTO ContractHistory SELECT * FROM Contracts WHERE Id = @id
// 2. UPDATE Contracts SET Amount = @new, ValidFrom = @now
```

### 5.4 Append Only

```csharp
// Never update, always insert new versions
entity.HasTemporalValidity(e => e.ValidFrom, e => e.ValidTo)
      .WithVersioningMode(VersioningMode.AppendOnly);

// All changes create new records, old records immutable
```

---

## 6. Temporal Relationships (1:M, M:M)

### 6.1 The Challenge

When parent and child entities both have temporal validity:

```csharp
// Contract valid: 2024-01-01 to 2024-12-31
// LineItem v1: 2024-01-01 to 2024-06-30
// LineItem v2: 2024-07-01 to 2024-12-31

var asOf = new DateTime(2024, 3, 15);
var contract = await db.Contracts
    .ValidAt(asOf)
    .Include(c => c.LineItems) // Which version of LineItems?
    .FirstAsync();
```

### 6.2 Solution: Temporal Include

```csharp
// Include with same temporal context
var contract = await db.Contracts
    .ValidAt(asOf)
    .IncludeValidAt(c => c.LineItems, asOf)
    .FirstAsync();

// Fluent temporal scope
var contract = await db.AsOf(asOf)
    .Query<Contract>()
    .Include(c => c.LineItems) // Auto-applies same temporal filter
    .FirstAsync();
```

### 6.3 Configuration Options

```csharp
// Parent-child temporal containment
entity.HasTemporalValidity(e => e.ValidFrom, e => e.ValidTo)
      .WithChildContainment(TemporalContainment.Strict);
// Children must be within parent validity

// Independent validity
entity.HasTemporalValidity(e => e.ValidFrom, e => e.ValidTo)
      .WithChildContainment(TemporalContainment.Independent);
// Children can have any validity
```

---

## 7. Querying Across Temporal Relationships

### 7.1 Projection with Temporal Filtering

```csharp
var result = await db.Contracts
    .ValidAt(asOfDate)
    .Select(c => new ContractDto
    {
        Id = c.Id,
        ContractNumber = c.ContractNumber,
        LineItems = c.LineItems
            .Where(li => li.ValidFrom <= asOfDate && 
                        (li.ValidTo == null || li.ValidTo > asOfDate))
            .Select(li => new LineItemDto { ... })
            .ToList()
    })
    .FirstAsync();
```

### 7.2 Temporal Join Helper

```csharp
// Join with temporal alignment
var result = await db.TemporalJoin<Contract, LineItem>(
    asOfDate,
    (c, li) => c.Id == li.ContractId)
    .Select(x => new { x.Contract, x.LineItem })
    .ToListAsync();
```

---

## 8. Temporal + Sharding Interaction

### 8.1 Independent Configuration

```csharp
// Sharding by one property, temporal by another
entity.ShardBy(c => c.Region);                    // Shard key
entity.HasTemporalValidity(c => c.ValidFrom, c => c.ValidTo);  // Temporal key
```

### 8.2 Aligned Configuration

```csharp
// Same property for both (common pattern)
entity.ShardByDate(c => c.EffectiveDate, DateInterval.Year);
entity.HasTemporalValidity(c => c.EffectiveDate, c => c.ExpirationDate);
```

### 8.3 Query Resolution

```csharp
// DTDE combines both:
var contracts = await db.Contracts
    .ValidAt(new DateTime(2024, 6, 15))  // Temporal filter
    .Where(c => c.Region == "EU")        // Shard hint
    .ToListAsync();

// Resolution:
// 1. Temporal: EffectiveDate <= 2024-06-15 AND (ExpirationDate > 2024-06-15 OR NULL)
// 2. Shard: Query Contracts_EU (or EU database)
// 3. Date Shard: Query Contracts_2024 (if date-sharded)
```

---

## 9. Configuration Summary

| Feature | Configuration |
|---------|---------------|
| No temporal | Default (don't call `HasTemporalValidity`) |
| Basic temporal | `HasTemporalValidity(e => e.Start, e => e.End)` |
| Open-ended | `HasTemporalValidity(e => e.Start)` |
| Custom property names | Any property names work |
| Soft versioning | `.WithVersioningMode(SoftVersion)` |
| Audit trail | `.WithVersioningMode(AuditTrail)` |
| Append only | `.WithVersioningMode(AppendOnly)` |
| Child containment | `.WithChildContainment(Strict)` |

---

## 10. Migration from Existing Code

### 10.1 Adding Temporal to Existing Entity

```csharp
// Step 1: Add temporal properties to entity
public DateTime ValidFrom { get; set; }
public DateTime? ValidTo { get; set; }

// Step 2: Configure (backfill existing data first)
entity.HasTemporalValidity(e => e.ValidFrom, e => e.ValidTo);

// Step 3: Backfill migration
UPDATE MyTable SET ValidFrom = CreatedAt, ValidTo = NULL WHERE ValidFrom IS NULL;
```

### 10.2 Removing Temporal

```csharp
// Simply remove the configuration
// entity.HasTemporalValidity(...); // Remove this line

// Entity reverts to standard EF Core behavior
```
