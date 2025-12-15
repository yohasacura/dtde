# API Reference

Complete API reference for DTDE (Distributed Temporal Data Engine).

## Table of Contents

- [DbContext Extensions](#dbcontext-extensions)
- [Configuration API](#configuration-api)
- [Query Methods](#query-methods)
- [Cross-Shard Transactions](#cross-shard-transactions)
- [Entity Configuration](#entity-configuration)
- [Shard Configuration](#shard-configuration)

---

## DbContext Extensions

### DtdeDbContext

Base class for DTDE-enabled contexts.

```csharp
namespace Dtde.EntityFramework;

public abstract class DtdeDbContext : DbContext
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `TemporalContext` | `ITemporalContext` | Access to temporal context settings |
| `MetadataRegistry` | `IMetadataRegistry` | Registry of entity metadata |
| `ShardRegistry` | `IShardRegistry` | Registry of shard definitions |

#### Methods

##### ValidAt\<TEntity\>

Returns entities valid at a specific point in time.

```csharp
public IQueryable<TEntity> ValidAt<TEntity>(DateTime asOfDate) where TEntity : class
```

**Parameters:**
- `asOfDate` - The point in time to query

**Returns:** `IQueryable<TEntity>` filtered to valid entities

**Example:**
```csharp
var currentContracts = await db.ValidAt<Contract>(DateTime.Today).ToListAsync();
```

##### ValidBetween\<TEntity\>

Returns entities valid within a date range.

```csharp
public IQueryable<TEntity> ValidBetween<TEntity>(DateTime startDate, DateTime endDate)
    where TEntity : class
```

**Parameters:**
- `startDate` - Start of the range (inclusive)
- `endDate` - End of the range (inclusive)

**Returns:** `IQueryable<TEntity>` with entities valid at any point in the range

**Example:**
```csharp
var q1Contracts = await db.ValidBetween<Contract>(
    new DateTime(2024, 1, 1),
    new DateTime(2024, 3, 31))
    .ToListAsync();
```

##### AllVersions\<TEntity\>

Returns all versions of entities, bypassing temporal filtering.

```csharp
public IQueryable<TEntity> AllVersions<TEntity>() where TEntity : class
```

**Returns:** `IQueryable<TEntity>` with all entity versions

**Example:**
```csharp
var history = await db.AllVersions<Contract>()
    .Where(c => c.ContractNumber == "CTR-001")
    .OrderBy(c => c.EffectiveDate)
    .ToListAsync();
```

---

## Configuration API

### DbContextOptionsBuilder Extensions

#### UseDtde

Configures DTDE for the DbContext.

```csharp
public static DbContextOptionsBuilder UseDtde(
    this DbContextOptionsBuilder optionsBuilder,
    Action<DtdeOptionsBuilder> configureOptions)
```

**Parameters:**
- `configureOptions` - Action to configure DTDE options

**Returns:** The options builder for chaining

**Example:**
```csharp
services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
    options.UseDtde(dtde =>
    {
        dtde.SetMaxParallelShards(10);
        dtde.EnableDiagnostics();
    });
});
```

#### UseDtde (with pre-built options)

```csharp
public static DbContextOptionsBuilder UseDtde(
    this DbContextOptionsBuilder optionsBuilder,
    DtdeOptions options)
```

### DtdeOptionsBuilder

Builder for configuring DTDE options.

```csharp
namespace Dtde.EntityFramework.Configuration;

public sealed class DtdeOptionsBuilder
```

#### Methods

##### AddShard (Action)

Adds a shard using a builder action.

```csharp
public DtdeOptionsBuilder AddShard(Action<ShardMetadataBuilder> configure)
```

**Example:**
```csharp
dtde.AddShard(s => s
    .WithId("EU")
    .WithShardKeyValue("EU")
    .WithTable("Customers_EU", "dbo")
    .WithTier(ShardTier.Hot));
```

##### AddShard (IShardMetadata)

Adds a pre-built shard metadata.

```csharp
public DtdeOptionsBuilder AddShard(IShardMetadata shard)
```

##### AddShardsFromConfig

Loads shards from a JSON configuration file.

```csharp
public DtdeOptionsBuilder AddShardsFromConfig(string configPath)
```

**Parameters:**
- `configPath` - Path to the JSON configuration file

**Example:**
```csharp
dtde.AddShardsFromConfig("shards.json");
```

**JSON Format:**
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
    }
  ]
}
```

##### ConfigureEntity\<TEntity\>

Configures entity-specific metadata.

```csharp
public DtdeOptionsBuilder ConfigureEntity<TEntity>(
    Action<EntityMetadataBuilder<TEntity>> configure)
    where TEntity : class
```

##### SetMaxParallelShards

Sets maximum concurrent shard queries.

```csharp
public DtdeOptionsBuilder SetMaxParallelShards(int maxParallel)
```

**Parameters:**
- `maxParallel` - Maximum concurrent queries (default: 10)

##### EnableDiagnostics

Enables diagnostic logging.

```csharp
public DtdeOptionsBuilder EnableDiagnostics()
```

##### EnableTestMode

Enables test mode (single shard, no distribution).

```csharp
public DtdeOptionsBuilder EnableTestMode()
```

##### SetDefaultTemporalContext

Sets default temporal context provider.

```csharp
public DtdeOptionsBuilder SetDefaultTemporalContext(Func<DateTime> provider)
```

**Example:**
```csharp
dtde.SetDefaultTemporalContext(() => DateTime.UtcNow);
```

---

## Cross-Shard Transactions

### ICrossShardTransactionCoordinator

Coordinates transactions that span multiple database shards.

```csharp
namespace Dtde.Abstractions.Transactions;

public interface ICrossShardTransactionCoordinator
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `CurrentTransaction` | `ICrossShardTransaction?` | The active cross-shard transaction, if any |

#### Methods

##### BeginTransactionAsync

Starts a new cross-shard transaction.

```csharp
Task<ICrossShardTransaction> BeginTransactionAsync(
    CrossShardTransactionOptions? options = null,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `options` - Optional configuration for the transaction
- `cancellationToken` - Cancellation token

**Returns:** A new cross-shard transaction

**Example:**
```csharp
await using var transaction = await coordinator.BeginTransactionAsync();
await transaction.EnlistAsync("shard-eu");
await transaction.EnlistAsync("shard-us");
// Perform operations...
await transaction.CommitAsync();
```

##### ExecuteInTransactionAsync

Executes an action within a cross-shard transaction with automatic commit/rollback.

```csharp
Task ExecuteInTransactionAsync(
    Func<ICrossShardTransaction, Task> action,
    CrossShardTransactionOptions? options = null,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `action` - The action to execute within the transaction
- `options` - Optional configuration
- `cancellationToken` - Cancellation token

**Example:**
```csharp
await coordinator.ExecuteInTransactionAsync(async tx =>
{
    await tx.EnlistAsync("shard-eu");
    await tx.EnlistAsync("shard-us");

    // Modify data in both shards
    await context.SaveChangesAsync();
});
```

##### ExecuteInTransactionAsync\<TResult\>

Executes a function within a transaction and returns a result.

```csharp
Task<TResult> ExecuteInTransactionAsync<TResult>(
    Func<ICrossShardTransaction, Task<TResult>> func,
    CrossShardTransactionOptions? options = null,
    CancellationToken cancellationToken = default)
```

##### RecoverAsync

Recovers in-doubt transactions after a failure.

```csharp
Task<int> RecoverAsync(CancellationToken cancellationToken = default)
```

**Returns:** Number of transactions recovered

---

### ICrossShardTransaction

Represents an active cross-shard transaction.

```csharp
namespace Dtde.Abstractions.Transactions;

public interface ICrossShardTransaction : IAsyncDisposable
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `TransactionId` | `string` | Unique transaction identifier |
| `State` | `TransactionState` | Current transaction state |
| `IsolationLevel` | `CrossShardIsolationLevel` | Isolation level |
| `Timeout` | `TimeSpan` | Transaction timeout |
| `EnlistedShards` | `IReadOnlyCollection<string>` | Enlisted shard IDs |

#### Methods

##### EnlistAsync

Enlists a shard in the transaction.

```csharp
Task EnlistAsync(string shardId, CancellationToken cancellationToken = default)
```

**Parameters:**
- `shardId` - The shard ID to enlist

**Example:**
```csharp
await transaction.EnlistAsync("shard-eu");
```

##### CommitAsync

Commits the transaction using two-phase commit.

```csharp
Task CommitAsync(CancellationToken cancellationToken = default)
```

##### RollbackAsync

Rolls back the transaction on all enlisted shards.

```csharp
Task RollbackAsync(CancellationToken cancellationToken = default)
```

##### GetParticipant

Gets a transaction participant for a specific shard.

```csharp
ITransactionParticipant? GetParticipant(string shardId)
```

---

### CrossShardTransactionOptions

Configuration options for cross-shard transactions.

```csharp
namespace Dtde.Abstractions.Transactions;

public class CrossShardTransactionOptions
```

#### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Timeout` | `TimeSpan` | 30 seconds | Transaction timeout |
| `IsolationLevel` | `CrossShardIsolationLevel` | `ReadCommitted` | Isolation level |
| `EnableRetry` | `bool` | `true` | Enable automatic retry |
| `MaxRetryAttempts` | `int` | `3` | Maximum retry attempts |
| `RetryDelay` | `TimeSpan` | 100ms | Initial retry delay |
| `UseExponentialBackoff` | `bool` | `true` | Use exponential backoff |
| `MaxRetryDelay` | `TimeSpan` | 10 seconds | Maximum retry delay |
| `TransactionName` | `string?` | `null` | Optional name for logging |
| `EnableRecovery` | `bool` | `false` | Enable transaction recovery |

#### Static Presets

```csharp
// Default settings
public static CrossShardTransactionOptions Default { get; }

// Short-lived: 10s timeout, 2 retries, no recovery
public static CrossShardTransactionOptions ShortLived { get; }

// Long-running: 5min timeout, 5 retries, recovery enabled
public static CrossShardTransactionOptions LongRunning { get; }
```

**Example:**
```csharp
var options = new CrossShardTransactionOptions
{
    Timeout = TimeSpan.FromMinutes(2),
    IsolationLevel = CrossShardIsolationLevel.Serializable,
    TransactionName = "FundsTransfer"
};

await coordinator.ExecuteInTransactionAsync(async tx =>
{
    // Operations
}, options);
```

---

### TransactionState

Enum representing transaction states.

```csharp
public enum TransactionState
{
    Active,       // Transaction is open
    Preparing,    // Two-phase commit in progress
    Committed,    // Successfully committed
    RolledBack,   // Rolled back
    Failed        // Failed during commit
}
```

### CrossShardIsolationLevel

Isolation levels for cross-shard transactions.

```csharp
public enum CrossShardIsolationLevel
{
    ReadUncommitted,
    ReadCommitted,
    RepeatableRead,
    Serializable,
    Snapshot
}
```

---

## Query Methods

### IShardedQueryExecutor

Interface for executing queries across shards.

```csharp
namespace Dtde.EntityFramework.Query;

public interface IShardedQueryExecutor
```

#### Methods

##### ExecuteAsync\<TEntity\>

Executes a query across all relevant shards.

```csharp
Task<IReadOnlyList<TEntity>> ExecuteAsync<TEntity>(
    IQueryable<TEntity> query,
    CancellationToken cancellationToken = default)
    where TEntity : class
```

**Parameters:**
- `query` - The LINQ query to execute
- `cancellationToken` - Cancellation token

**Returns:** Combined results from all shards

##### ExecuteScalarAsync\<TEntity, TResult\>

Executes a scalar aggregation across shards.

```csharp
Task<TResult> ExecuteScalarAsync<TEntity, TResult>(
    IQueryable<TEntity> query,
    Func<IEnumerable<TResult>, TResult> aggregator,
    CancellationToken cancellationToken = default)
    where TEntity : class
```

**Parameters:**
- `query` - The LINQ query
- `aggregator` - Function to aggregate shard results
- `cancellationToken` - Cancellation token

---

## Entity Configuration

### EntityTypeBuilder Extensions

Extensions for configuring sharding on entities.

#### ShardBy

Configures property-based sharding.

```csharp
public static ShardingConfigurationBuilder<TEntity> ShardBy<TEntity, TProperty>(
    this EntityTypeBuilder<TEntity> builder,
    Expression<Func<TEntity, TProperty>> propertyExpression)
    where TEntity : class
```

**Example:**
```csharp
modelBuilder.Entity<Customer>(entity =>
{
    entity.ShardBy(c => c.Region);
});
```

#### ShardByHash

Configures hash-based sharding.

```csharp
public static ShardingConfigurationBuilder<TEntity> ShardByHash<TEntity, TProperty>(
    this EntityTypeBuilder<TEntity> builder,
    Expression<Func<TEntity, TProperty>> propertyExpression,
    int shardCount)
    where TEntity : class
```

**Parameters:**
- `propertyExpression` - Property to hash
- `shardCount` - Number of hash buckets/shards

**Example:**
```csharp
entity.ShardByHash(c => c.Id, shardCount: 8);
```

#### ShardByDate

Configures date-based sharding.

```csharp
public static ShardingConfigurationBuilder<TEntity> ShardByDate<TEntity>(
    this EntityTypeBuilder<TEntity> builder,
    Expression<Func<TEntity, DateTime>> dateProperty,
    DateShardInterval interval = DateShardInterval.Year)
    where TEntity : class
```

**Parameters:**
- `dateProperty` - DateTime property to shard by
- `interval` - Date interval (Year, Quarter, Month, Day)

**Example:**
```csharp
entity.ShardByDate(o => o.CreatedAt, DateShardInterval.Year);
```

#### HasTemporalValidity

Configures temporal validity for an entity.

```csharp
public static TemporalConfigurationBuilder<TEntity> HasTemporalValidity<TEntity>(
    this EntityTypeBuilder<TEntity> builder,
    Expression<Func<TEntity, DateTime>> validFrom,
    Expression<Func<TEntity, DateTime?>> validTo)
    where TEntity : class
```

**Parameters:**
- `validFrom` - Expression selecting the valid-from property
- `validTo` - Expression selecting the valid-to property (nullable)

**Example:**
```csharp
entity.HasTemporalValidity(
    validFrom: c => c.EffectiveDate,
    validTo: c => c.ExpirationDate);
```

### ShardingConfigurationBuilder

Builder for sharding configuration.

#### WithStorageMode

Sets the storage mode for shards.

```csharp
public ShardingConfigurationBuilder<TEntity> WithStorageMode(ShardStorageMode mode)
```

**Parameters:**
- `mode` - `ShardStorageMode.Tables` or `ShardStorageMode.Databases`

### TemporalConfigurationBuilder

Builder for temporal configuration.

#### WithVersioningMode

Sets the versioning behavior.

```csharp
public TemporalConfigurationBuilder<TEntity> WithVersioningMode(VersioningMode mode)
```

**Parameters:**
- `mode` - `SoftVersion`, `AuditTrail`, or `AppendOnly`

#### WithHistoryTable

Configures a separate history table (for AuditTrail mode).

```csharp
public TemporalConfigurationBuilder<TEntity> WithHistoryTable(string tableName)
```

#### WithOpenEndedValue

Sets the value used for open-ended records.

```csharp
public TemporalConfigurationBuilder<TEntity> WithOpenEndedValue(DateTime value)
```

---

## Shard Configuration

### ShardMetadataBuilder

Builder for creating shard metadata.

```csharp
namespace Dtde.Core.Metadata;

public class ShardMetadataBuilder
```

#### Methods

##### WithId

Sets the unique shard identifier.

```csharp
public ShardMetadataBuilder WithId(string shardId)
```

##### WithName

Sets the display name.

```csharp
public ShardMetadataBuilder WithName(string name)
```

##### WithShardKeyValue

Sets the shard key value this shard handles.

```csharp
public ShardMetadataBuilder WithShardKeyValue(string keyValue)
```

##### WithTable

Configures table-based sharding.

```csharp
public ShardMetadataBuilder WithTable(string tableName, string schemaName = "dbo")
```

##### WithConnectionString

Configures database-based sharding.

```csharp
public ShardMetadataBuilder WithConnectionString(string connectionString)
```

##### WithDateRange

Sets the date range this shard covers.

```csharp
public ShardMetadataBuilder WithDateRange(DateTime start, DateTime end)
```

##### WithTier

Sets the storage tier.

```csharp
public ShardMetadataBuilder WithTier(ShardTier tier)
```

**Values:**
- `ShardTier.Hot` - Active, fast storage
- `ShardTier.Warm` - Less active data
- `ShardTier.Cold` - Archived data
- `ShardTier.Archive` - Long-term storage

##### WithPriority

Sets query priority (higher = queried first).

```csharp
public ShardMetadataBuilder WithPriority(int priority)
```

##### AsReadOnly

Marks the shard as read-only.

```csharp
public ShardMetadataBuilder AsReadOnly()
```

##### Build

Creates the shard metadata.

```csharp
public IShardMetadata Build()
```

**Example:**
```csharp
var shard = new ShardMetadataBuilder()
    .WithId("archive-2020")
    .WithName("2020 Archive")
    .WithTable("Orders_2020", "archive")
    .WithDateRange(new DateTime(2020, 1, 1), new DateTime(2020, 12, 31))
    .WithTier(ShardTier.Archive)
    .AsReadOnly()
    .WithPriority(10)
    .Build();
```

### ShardMetadata Static Methods

Factory methods for common scenarios.

#### ForTable

Creates a table-based shard.

```csharp
public static ShardMetadata ForTable(
    string shardId,
    string tableName,
    string? shardKeyValue = null,
    string schemaName = "dbo")
```

#### ForDatabase

Creates a database-based shard.

```csharp
public static ShardMetadata ForDatabase(
    string shardId,
    string name,
    string connectionString,
    string? shardKeyValue = null)
```

---

## Enumerations

### ShardStorageMode

```csharp
public enum ShardStorageMode
{
    Tables,     // Multiple tables in same database
    Databases   // Separate databases
}
```

### ShardTier

```csharp
public enum ShardTier
{
    Hot,        // Active data, fast storage
    Warm,       // Less active data
    Cold,       // Archived data
    Archive     // Long-term storage
}
```

### DateShardInterval

```csharp
public enum DateShardInterval
{
    Year,
    Month,
    Quarter,
    Week
}
```

### VersioningMode

```csharp
public enum VersioningMode
{
    SoftVersion,    // Close old, create new
    AuditTrail,     // Copy to history, update current
    AppendOnly      // Never update, always insert
}
```

---

## Next Steps

- [Configuration](configuration.md) - Detailed configuration options
- [Classes Reference](classes-reference.md) - Complete class documentation
- [Troubleshooting](troubleshooting.md) - Common issues and solutions

---

[← Back to Wiki](index.md) | [Configuration →](configuration.md)
