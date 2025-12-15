# Classes Reference

Detailed documentation for all DTDE classes, interfaces, and their members.

## Table of Contents

- [Core Classes](#core-classes)
- [Metadata Classes](#metadata-classes)
- [Query Classes](#query-classes)
- [Update Classes](#update-classes)
- [Transaction Classes](#transaction-classes)
- [Configuration Classes](#configuration-classes)
- [Interfaces](#interfaces)

---

## Core Classes

### DtdeDbContext

Base DbContext providing DTDE functionality.

```csharp
namespace Dtde.EntityFramework;

public abstract class DtdeDbContext : DbContext
```

#### Constructors

```csharp
/// <summary>
/// Initializes a new instance with default options.
/// </summary>
protected DtdeDbContext()

/// <summary>
/// Initializes a new instance with specified options.
/// </summary>
/// <param name="options">The DbContext options.</param>
protected DtdeDbContext(DbContextOptions options)
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `TemporalContext` | `ITemporalContext` | Access temporal context settings |
| `MetadataRegistry` | `IMetadataRegistry` | Registry of entity metadata |
| `ShardRegistry` | `IShardRegistry` | Registry of shard definitions |

#### Methods

##### ValidAt\<TEntity\>

```csharp
/// <summary>
/// Gets a queryable filtered to entities valid at the specified date.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <param name="asOfDate">The point in time.</param>
/// <returns>A queryable filtered to valid entities.</returns>
public IQueryable<TEntity> ValidAt<TEntity>(DateTime asOfDate)
    where TEntity : class
```

**Behavior:**
- Returns entities where `ValidFrom <= asOfDate AND (ValidTo IS NULL OR ValidTo > asOfDate)`
- For non-temporal entities, returns all records

##### ValidBetween\<TEntity\>

```csharp
/// <summary>
/// Gets a queryable filtered to entities valid within the specified date range.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <param name="startDate">The start of the range.</param>
/// <param name="endDate">The end of the range.</param>
/// <returns>A queryable filtered to valid entities.</returns>
public IQueryable<TEntity> ValidBetween<TEntity>(DateTime startDate, DateTime endDate)
    where TEntity : class
```

**Behavior:**
- Returns entities valid at any point within the range
- Filter: `ValidFrom <= endDate AND (ValidTo IS NULL OR ValidTo >= startDate)`

##### AllVersions\<TEntity\>

```csharp
/// <summary>
/// Gets all versions of entities, bypassing temporal filtering.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <returns>A queryable with all entity versions.</returns>
public IQueryable<TEntity> AllVersions<TEntity>()
    where TEntity : class
```

---

## Metadata Classes

### ShardMetadata

Metadata describing a single shard.

```csharp
namespace Dtde.Core.Metadata;

public sealed class ShardMetadata : IShardMetadata
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ShardId` | `string` | Unique identifier |
| `Name` | `string` | Display name |
| `StorageMode` | `ShardStorageMode` | Tables or Databases |
| `TableName` | `string?` | Table name (table mode) |
| `SchemaName` | `string?` | Schema name |
| `ConnectionString` | `string?` | Connection (database mode) |
| `ShardKeyValue` | `string?` | Key value this shard handles |
| `DateRange` | `DateRange?` | Date range coverage |
| `KeyRange` | `KeyRange?` | Numeric key range |
| `Tier` | `ShardTier` | Storage tier |
| `IsReadOnly` | `bool` | Read-only flag |
| `Priority` | `int` | Query priority |

#### Static Factory Methods

```csharp
/// <summary>
/// Creates a table shard with the specified table name.
/// </summary>
public static ShardMetadata ForTable(
    string shardId,
    string tableName,
    string? shardKeyValue = null,
    string schemaName = "dbo")

/// <summary>
/// Creates a database shard with the specified connection string.
/// </summary>
public static ShardMetadata ForDatabase(
    string shardId,
    string name,
    string connectionString,
    string? shardKeyValue = null)
```

### ShardMetadataBuilder

Fluent builder for creating ShardMetadata.

```csharp
namespace Dtde.Core.Metadata;

public class ShardMetadataBuilder
```

#### Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `WithId(string)` | `ShardMetadataBuilder` | Sets shard ID |
| `WithName(string)` | `ShardMetadataBuilder` | Sets display name |
| `WithShardKeyValue(string)` | `ShardMetadataBuilder` | Sets key value |
| `WithTable(string, string)` | `ShardMetadataBuilder` | Configures table sharding |
| `WithConnectionString(string)` | `ShardMetadataBuilder` | Configures database sharding |
| `WithDateRange(DateTime, DateTime)` | `ShardMetadataBuilder` | Sets date range |
| `WithTier(ShardTier)` | `ShardMetadataBuilder` | Sets storage tier |
| `WithPriority(int)` | `ShardMetadataBuilder` | Sets priority |
| `AsReadOnly()` | `ShardMetadataBuilder` | Marks as read-only |
| `Build()` | `IShardMetadata` | Creates the metadata |

### ShardRegistry

Collection of available shards.

```csharp
namespace Dtde.Core.Metadata;

public class ShardRegistry : IShardRegistry
```

#### Methods

```csharp
/// <summary>
/// Gets all registered shards.
/// </summary>
public IReadOnlyList<IShardMetadata> GetAllShards()

/// <summary>
/// Gets a shard by ID.
/// </summary>
public IShardMetadata? GetShard(string shardId)

/// <summary>
/// Gets shards covering a date range.
/// </summary>
public IEnumerable<IShardMetadata> GetShardsForDateRange(DateTime startDate, DateTime endDate)

/// <summary>
/// Gets all writable shards (not read-only).
/// </summary>
public IEnumerable<IShardMetadata> GetWritableShards()

/// <summary>
/// Gets shards by storage tier.
/// </summary>
public IEnumerable<IShardMetadata> GetShardsByTier(ShardTier tier)

/// <summary>
/// Adds a shard to the registry.
/// </summary>
public void AddShard(IShardMetadata shard)
```

### DateRange

Represents a date range for shard coverage.

```csharp
namespace Dtde.Core.Metadata;

public readonly struct DateRange
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Start` | `DateTime` | Start date (inclusive) |
| `End` | `DateTime` | End date (exclusive) |

#### Methods

```csharp
/// <summary>
/// Checks if a date falls within this range.
/// </summary>
public bool Contains(DateTime date)

/// <summary>
/// Checks if this range intersects with another.
/// </summary>
public bool Intersects(DateRange other)

/// <summary>
/// Gets the intersection of two ranges.
/// </summary>
public DateRange? Intersection(DateRange other)
```

### EntityMetadata

Metadata describing an entity's DTDE configuration.

```csharp
namespace Dtde.Core.Metadata;

public sealed class EntityMetadata : IEntityMetadata
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `EntityType` | `Type` | The CLR type |
| `ShardingConfiguration` | `IShardingConfiguration?` | Sharding settings |
| `ValidityConfiguration` | `IValidityConfiguration?` | Temporal settings |
| `Properties` | `IReadOnlyList<IPropertyMetadata>` | Property metadata |
| `Relations` | `IReadOnlyList<IRelationMetadata>` | Relationship metadata |

### MetadataRegistry

Registry of entity metadata.

```csharp
namespace Dtde.Core.Metadata;

public class MetadataRegistry : IMetadataRegistry
```

#### Methods

```csharp
/// <summary>
/// Gets metadata for an entity type.
/// </summary>
public IEntityMetadata? GetEntityMetadata<TEntity>() where TEntity : class

/// <summary>
/// Gets metadata for an entity type.
/// </summary>
public IEntityMetadata? GetEntityMetadata(Type entityType)

/// <summary>
/// Registers entity metadata.
/// </summary>
public void RegisterEntity(IEntityMetadata metadata)
```

---

## Query Classes

### ShardedQueryExecutor

Executes queries across multiple shards.

```csharp
namespace Dtde.EntityFramework.Query;

public sealed class ShardedQueryExecutor : IShardedQueryExecutor
```

#### Constructor

```csharp
public ShardedQueryExecutor(
    IShardRegistry shardRegistry,
    IMetadataRegistry metadataRegistry,
    ITemporalContext temporalContext,
    IShardContextFactory shardContextFactory,
    ILogger<ShardedQueryExecutor> logger)
```

#### Methods

```csharp
/// <summary>
/// Executes a query across all relevant shards.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <param name="query">The LINQ query to execute.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>Combined results from all shards.</returns>
public async Task<IReadOnlyList<TEntity>> ExecuteAsync<TEntity>(
    IQueryable<TEntity> query,
    CancellationToken cancellationToken = default)
    where TEntity : class

/// <summary>
/// Executes a scalar aggregation across shards.
/// </summary>
public async Task<TResult> ExecuteScalarAsync<TEntity, TResult>(
    IQueryable<TEntity> query,
    Func<IEnumerable<TResult>, TResult> aggregator,
    CancellationToken cancellationToken = default)
    where TEntity : class
```

### ShardContextFactory

Creates DbContext instances for specific shards.

```csharp
namespace Dtde.EntityFramework.Query;

public class ShardContextFactory : IShardContextFactory
```

#### Methods

```csharp
/// <summary>
/// Creates a DbContext for a specific shard.
/// </summary>
public async Task<DbContext> CreateContextAsync(
    IShardMetadata shard,
    CancellationToken cancellationToken = default)
```

### DtdeExpressionRewriter

Rewrites LINQ expressions for shard-specific execution.

```csharp
namespace Dtde.EntityFramework.Query;

public class DtdeExpressionRewriter : IExpressionRewriter
```

#### Methods

```csharp
/// <summary>
/// Rewrites an expression for execution on a specific shard.
/// </summary>
public Expression Rewrite(Expression expression, IShardMetadata shard)
```

---

## Update Classes

### ShardWriteRouter

Routes write operations to appropriate shards.

```csharp
namespace Dtde.EntityFramework.Update;

public sealed class ShardWriteRouter
```

#### Constructor

```csharp
public ShardWriteRouter(
    IShardRegistry shardRegistry,
    IMetadataRegistry metadataRegistry,
    ILogger<ShardWriteRouter> logger)
```

#### Methods

```csharp
/// <summary>
/// Resolves the target shard for an entity.
/// </summary>
public IShardMetadata ResolveTargetShard<TEntity>(TEntity entity)
    where TEntity : class

/// <summary>
/// Routes all tracked changes to appropriate shards.
/// </summary>
public void RouteChanges(ChangeTracker changeTracker)
```

### DtdeUpdateProcessor

Processes entity updates with temporal versioning.

```csharp
namespace Dtde.EntityFramework.Update;

public sealed class DtdeUpdateProcessor : IDtdeUpdateProcessor
```

#### Methods

```csharp
/// <summary>
/// Processes changes before SaveChanges.
/// </summary>
public void ProcessChanges(ChangeTracker changeTracker)

/// <summary>
/// Creates a new version of an entity.
/// </summary>
public TEntity CreateNewVersion<TEntity>(
    TEntity entity,
    Action<TEntity> applyChanges,
    DateTime effectiveFrom)
    where TEntity : class
```

### VersionManager

Manages entity version creation.

```csharp
namespace Dtde.EntityFramework.Update;

public class VersionManager
```

#### Methods

```csharp
/// <summary>
/// Creates a new version of a temporal entity.
/// </summary>
public TEntity CreateVersion<TEntity>(
    TEntity original,
    DateTime effectiveFrom,
    DateTime? expirationDate = null)
    where TEntity : class, new()

/// <summary>
/// Closes the validity of an entity.
/// </summary>
public void CloseValidity<TEntity>(
    TEntity entity,
    DateTime closureDate)
    where TEntity : class
```

---

## Transaction Classes

### CrossShardTransactionCoordinator

Coordinates transactions across multiple database shards.

```csharp
namespace Dtde.Core.Transactions;

public class CrossShardTransactionCoordinator : ICrossShardTransactionCoordinator
```

#### Constructor

```csharp
public CrossShardTransactionCoordinator(
    IShardRegistry shardRegistry,
    Func<string, CancellationToken, Task<DbContext>> contextFactory,
    ILogger<CrossShardTransactionCoordinator> coordinatorLogger,
    ILogger<CrossShardTransaction> transactionLogger)
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `CurrentTransaction` | `ICrossShardTransaction?` | Active transaction |

#### Methods

```csharp
/// <summary>
/// Begins a new cross-shard transaction.
/// </summary>
public async Task<ICrossShardTransaction> BeginTransactionAsync(
    CrossShardTransactionOptions? options = null,
    CancellationToken cancellationToken = default)

/// <summary>
/// Executes an action within a transaction with automatic commit/rollback.
/// </summary>
public async Task ExecuteInTransactionAsync(
    Func<ICrossShardTransaction, Task> action,
    CrossShardTransactionOptions? options = null,
    CancellationToken cancellationToken = default)

/// <summary>
/// Executes a function within a transaction and returns a result.
/// </summary>
public async Task<TResult> ExecuteInTransactionAsync<TResult>(
    Func<ICrossShardTransaction, Task<TResult>> func,
    CrossShardTransactionOptions? options = null,
    CancellationToken cancellationToken = default)

/// <summary>
/// Recovers in-doubt transactions after a failure.
/// </summary>
public async Task<int> RecoverAsync(CancellationToken cancellationToken = default)
```

### CrossShardTransaction

Represents an active cross-shard transaction.

```csharp
namespace Dtde.Core.Transactions;

public class CrossShardTransaction : ICrossShardTransaction
```

#### Constructor

```csharp
public CrossShardTransaction(
    string transactionId,
    CrossShardTransactionOptions options,
    IShardRegistry shardRegistry,
    Func<string, CancellationToken, Task<DbContext>> contextFactory,
    ILogger<CrossShardTransaction> logger)
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `TransactionId` | `string` | Unique ID (format: `XS-{timestamp}-{guid}`) |
| `State` | `TransactionState` | Current state |
| `IsolationLevel` | `CrossShardIsolationLevel` | Isolation level |
| `Timeout` | `TimeSpan` | Transaction timeout |
| `EnlistedShards` | `IReadOnlyCollection<string>` | Enlisted shard IDs |

#### Methods

```csharp
/// <summary>
/// Enlists a shard in this transaction.
/// </summary>
public async Task EnlistAsync(
    string shardId,
    CancellationToken cancellationToken = default)

/// <summary>
/// Commits the transaction using two-phase commit.
/// </summary>
public async Task CommitAsync(CancellationToken cancellationToken = default)

/// <summary>
/// Rolls back the transaction on all enlisted shards.
/// </summary>
public async Task RollbackAsync(CancellationToken cancellationToken = default)

/// <summary>
/// Gets the transaction participant for a shard.
/// </summary>
public ITransactionParticipant? GetParticipant(string shardId)
```

### CrossShardTransactionOptions

Configuration options for cross-shard transactions.

```csharp
namespace Dtde.Abstractions.Transactions;

public class CrossShardTransactionOptions
```

#### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Timeout` | `TimeSpan` | 30s | Transaction timeout |
| `IsolationLevel` | `CrossShardIsolationLevel` | `ReadCommitted` | Isolation level |
| `EnableRetry` | `bool` | `true` | Enable retry |
| `MaxRetryAttempts` | `int` | `3` | Max retries |
| `RetryDelay` | `TimeSpan` | 100ms | Initial delay |
| `UseExponentialBackoff` | `bool` | `true` | Exponential backoff |
| `MaxRetryDelay` | `TimeSpan` | 10s | Max delay |
| `TransactionName` | `string?` | `null` | Name for logging |
| `EnableRecovery` | `bool` | `false` | Enable recovery |

#### Static Properties

| Property | Description |
|----------|-------------|
| `Default` | Default configuration |
| `ShortLived` | 10s timeout, 2 retries |
| `LongRunning` | 5min timeout, recovery enabled |

#### Static Fields

| Field | Type | Description |
|-------|------|-------------|
| `DefaultTimeout` | `TimeSpan` | Modifiable default timeout |
| `DefaultIsolationLevel` | `CrossShardIsolationLevel` | Modifiable default level |

### TransactionParticipant

Represents a shard's participation in a cross-shard transaction.

```csharp
namespace Dtde.Core.Transactions;

public class TransactionParticipant : ITransactionParticipant
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ShardId` | `string` | Shard identifier |
| `Context` | `DbContext` | Shard's DbContext |
| `Transaction` | `IDbContextTransaction?` | Local transaction |
| `State` | `ParticipantState` | Participant state |

#### Methods

```csharp
/// <summary>
/// Prepares the participant for commit (Phase 1).
/// </summary>
public async Task PrepareAsync(CancellationToken cancellationToken = default)

/// <summary>
/// Commits the local transaction (Phase 2).
/// </summary>
public async Task CommitAsync(CancellationToken cancellationToken = default)

/// <summary>
/// Rolls back the local transaction.
/// </summary>
public async Task RollbackAsync(CancellationToken cancellationToken = default)
```

### TransparentShardingInterceptor

EF Core interceptor for automatic cross-shard transaction handling.

```csharp
namespace Dtde.EntityFramework.Infrastructure;

public class TransparentShardingInterceptor : SaveChangesInterceptor, IDbTransactionInterceptor
```

#### Constructor

```csharp
public TransparentShardingInterceptor(
    IServiceProvider serviceProvider,
    ILogger<TransparentShardingInterceptor> logger)
```

#### Behavior

- Intercepts `SaveChanges` and `SaveChangesAsync`
- Detects when entities target multiple shards
- Automatically coordinates cross-shard transactions
- Skips handling when explicit transactions are active
- Resolves scoped services from DbContext's service provider

---

## Configuration Classes

### DtdeOptions

DTDE configuration options.

```csharp
namespace Dtde.EntityFramework.Configuration;

public sealed class DtdeOptions
```

See [Configuration Reference](configuration.md) for details.

### DtdeOptionsBuilder

Fluent builder for DTDE options.

```csharp
namespace Dtde.EntityFramework.Configuration;

public sealed class DtdeOptionsBuilder
```

#### Methods

| Method | Description |
|--------|-------------|
| `AddShard(Action<ShardMetadataBuilder>)` | Adds shard via builder |
| `AddShard(IShardMetadata)` | Adds pre-built shard |
| `AddShardsFromConfig(string)` | Loads from JSON file |
| `ConfigureEntity<T>(Action<>)` | Configures entity |
| `SetMaxParallelShards(int)` | Sets parallelism |
| `EnableDiagnostics()` | Enables logging |
| `EnableTestMode()` | Enables test mode |
| `SetDefaultTemporalContext(Func<DateTime>)` | Sets default time |
| `Build()` | Creates options |

### DtdeOptionsExtension

EF Core options extension for DTDE.

```csharp
namespace Dtde.EntityFramework.Infrastructure;

public class DtdeOptionsExtension : IDbContextOptionsExtension
```

---

## Interfaces

### IShardMetadata

```csharp
namespace Dtde.Abstractions.Metadata;

public interface IShardMetadata
{
    string ShardId { get; }
    string Name { get; }
    ShardStorageMode StorageMode { get; }
    string? TableName { get; }
    string? SchemaName { get; }
    string? ConnectionString { get; }
    string? ShardKeyValue { get; }
    DateRange? DateRange { get; }
    KeyRange? KeyRange { get; }
    ShardTier Tier { get; }
    bool IsReadOnly { get; }
    int Priority { get; }
}
```

### IShardRegistry

```csharp
namespace Dtde.Abstractions.Metadata;

public interface IShardRegistry
{
    IReadOnlyList<IShardMetadata> GetAllShards();
    IShardMetadata? GetShard(string shardId);
    IEnumerable<IShardMetadata> GetShardsForDateRange(DateTime startDate, DateTime endDate);
    IEnumerable<IShardMetadata> GetWritableShards();
    IEnumerable<IShardMetadata> GetShardsByTier(ShardTier tier);
    void AddShard(IShardMetadata shard);
}
```

### IMetadataRegistry

```csharp
namespace Dtde.Abstractions.Metadata;

public interface IMetadataRegistry
{
    IEntityMetadata? GetEntityMetadata<TEntity>() where TEntity : class;
    IEntityMetadata? GetEntityMetadata(Type entityType);
    void RegisterEntity(IEntityMetadata metadata);
}
```

### IEntityMetadata

```csharp
namespace Dtde.Abstractions.Metadata;

public interface IEntityMetadata
{
    Type EntityType { get; }
    IShardingConfiguration? ShardingConfiguration { get; }
    IValidityConfiguration? ValidityConfiguration { get; }
    IReadOnlyList<IPropertyMetadata> Properties { get; }
    IReadOnlyList<IRelationMetadata> Relations { get; }
}
```

### IShardingStrategy

```csharp
namespace Dtde.Abstractions.Metadata;

public interface IShardingStrategy
{
    string StrategyType { get; }
    IEnumerable<IShardMetadata> ResolveShards(
        Expression? predicate,
        IShardRegistry registry);
    IShardMetadata ResolveWriteShard(
        object entity,
        IShardRegistry registry);
}
```

### ITemporalContext

```csharp
namespace Dtde.Abstractions.Temporal;

public interface ITemporalContext
{
    DateTime? CurrentPoint { get; }
    bool IncludeHistory { get; }

    void SetTemporalContext(DateTime asOfDate);
    void EnableHistoryMode();
    void ClearContext();
}
```

### IShardedQueryExecutor

```csharp
namespace Dtde.EntityFramework.Query;

public interface IShardedQueryExecutor
{
    Task<IReadOnlyList<TEntity>> ExecuteAsync<TEntity>(
        IQueryable<TEntity> query,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    Task<TResult> ExecuteScalarAsync<TEntity, TResult>(
        IQueryable<TEntity> query,
        Func<IEnumerable<TResult>, TResult> aggregator,
        CancellationToken cancellationToken = default)
        where TEntity : class;
}
```

### IShardContextFactory

```csharp
namespace Dtde.EntityFramework.Query;

public interface IShardContextFactory
{
    Task<DbContext> CreateContextAsync(
        IShardMetadata shard,
        CancellationToken cancellationToken = default);
}
```

### IExpressionRewriter

```csharp
namespace Dtde.EntityFramework.Query;

public interface IExpressionRewriter
{
    Expression Rewrite(Expression expression, IShardMetadata shard);
}
```

---

## Enumerations

### ShardStorageMode

```csharp
public enum ShardStorageMode
{
    /// <summary>Multiple tables in the same database.</summary>
    Tables,

    /// <summary>Separate databases (same or different servers).</summary>
    Databases
}
```

### ShardTier

```csharp
public enum ShardTier
{
    /// <summary>Active data, fast storage.</summary>
    Hot,

    /// <summary>Less active data.</summary>
    Warm,

    /// <summary>Archived data, slow storage.</summary>
    Cold,

    /// <summary>Long-term storage.</summary>
    Archive
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
    /// <summary>Close old record, create new.</summary>
    SoftVersion,

    /// <summary>Copy to history table, update current.</summary>
    AuditTrail,

    /// <summary>Never update, always insert new.</summary>
    AppendOnly
}
```

---

## Next Steps

- [Troubleshooting](troubleshooting.md) - Common issues and solutions
- [API Reference](api-reference.md) - API summary
- [Architecture](architecture.md) - System design

---

[← Back to Wiki](index.md) | [Troubleshooting →](troubleshooting.md)
