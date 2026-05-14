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

#### Extension methods (`Dtde.EntityFramework.Extensions`)

These extensions on `DtdeDbContext` are the recommended public surface
for the lifecycle / transaction / bulk operations. They live in
`DtdeDbContextExtensions` and `BulkOperationsExtensions`.

```csharp
// Provisioning
public static Task EnsureAllShardsCreatedAsync(
    this DtdeDbContext context,
    CancellationToken cancellationToken = default);

// Cross-shard transactions
public static Task<ICrossShardTransaction> BeginCrossShardTransactionAsync(
    this DtdeDbContext context,
    CancellationToken cancellationToken = default);

public static Task<ICrossShardTransaction> BeginCrossShardTransactionAsync(
    this DtdeDbContext context,
    CrossShardTransactionOptions options,
    CancellationToken cancellationToken = default);

// Bulk operations
public static Task<int> BulkInsertAsync<TEntity>(
    this DtdeDbContext context,
    IEnumerable<TEntity> entities,
    CancellationToken cancellationToken = default)
    where TEntity : class;

public static Task<int> BulkDeleteAsync<TEntity>(
    this DtdeDbContext context,
    Expression<Func<TEntity, bool>> filter,
    CancellationToken cancellationToken = default)
    where TEntity : class;

// EF 7-9 signature:
public static Task<int> BulkUpdateAsync<TEntity>(
    this DtdeDbContext context,
    Expression<Func<TEntity, bool>> filter,
    Expression<Func<SetPropertyCalls<TEntity>, SetPropertyCalls<TEntity>>> setPropertyCalls,
    CancellationToken cancellationToken = default)
    where TEntity : class;

// EF 10 signature (selected via #if NET10_0_OR_GREATER):
public static Task<int> BulkUpdateAsync<TEntity>(
    this DtdeDbContext context,
    Expression<Func<TEntity, bool>> filter,
    Action<UpdateSettersBuilder<TEntity>> setters,
    CancellationToken cancellationToken = default)
    where TEntity : class;
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
| `ClrType` | `Type` | The CLR type of the entity. |
| `TableName` | `string` | The database table name. |
| `SchemaName` | `string` | The database schema name. |
| `PrimaryKey` | `IPropertyMetadata?` | Primary-key property, or `null` if EF will infer it. |
| `TemporalConfiguration` | `ITemporalConfiguration?` | Temporal-versioning configuration; `null` if the entity is not temporal. |
| `ShardingConfiguration` | `IShardingConfiguration?` | Sharding configuration; `null` if the entity is not distributed. |
| `IsTemporal` | `bool` | Convenience: `TemporalConfiguration is not null`. |
| `IsSharded` | `bool` | Convenience: `ShardingConfiguration is not null`. |

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

public sealed class CrossShardTransactionCoordinator : ICrossShardTransactionCoordinator
```

#### Constructors

```csharp
// With an explicit transaction log for crash recovery:
public CrossShardTransactionCoordinator(
    IShardRegistry shardRegistry,
    ShardParticipantFactory participantFactory,
    ITransactionLog? transactionLog,
    ILogger<CrossShardTransactionCoordinator> logger,
    ILogger<CrossShardTransaction> transactionLogger);

// Without a log (uses null):
public CrossShardTransactionCoordinator(
    IShardRegistry shardRegistry,
    ShardParticipantFactory participantFactory,
    ILogger<CrossShardTransactionCoordinator> logger,
    ILogger<CrossShardTransaction> transactionLogger);
```

The `ShardParticipantFactory` delegate is provided by the EntityFramework
layer so the relational `BeginTransactionAsync(IsolationLevel, ...)`
overload can be used without leaking a relational reference into
`Dtde.Core`.

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

Represents an active cross-shard transaction. The 2PC protocol runs
across enlisted participants; a single-shard transaction skips prepare
(fast path).

```csharp
namespace Dtde.Core.Transactions;

public sealed class CrossShardTransaction : ICrossShardTransaction
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `TransactionId` | `string` | Unique ID (format: `XS-{timestamp}-{guid}`). |
| `State` | `TransactionState` | Current state. |
| `IsolationLevel` | `CrossShardIsolationLevel` | Effective isolation level. |
| `Timeout` | `TimeSpan` | Transaction timeout. |
| `CreatedAt` | `DateTime` | UTC timestamp when this transaction was created. |
| `EnlistedShards` | `IReadOnlyCollection<string>` | Participant keys (fully-qualified `group::id`). |
| `IsDisposed` | `bool` | Idempotent disposal flag; checked by the coordinator's `CurrentTransaction` to ignore stale ambient transactions left over from previous scopes. |

#### Methods

```csharp
/// <summary>
/// Enlists a shard by its fully-qualified id or default-group local id.
/// </summary>
public Task EnlistAsync(string shardId, CancellationToken cancellationToken = default);

/// <summary>
/// Enlists a shard via its metadata. Uses ToQualifiedId() so
/// same-local-id-different-group shards don't alias.
/// </summary>
public Task EnlistAsync(IShardMetadata shard, CancellationToken cancellationToken = default);

/// <summary>
/// Gets or creates the participant for the given shard. The common
/// way to drive writes inside a transaction.
/// </summary>
public Task<ShardTransactionParticipant> GetOrCreateParticipantAsync(
    string shardId, CancellationToken cancellationToken = default);

public Task<ShardTransactionParticipant> GetOrCreateParticipantAsync(
    IShardMetadata shard, CancellationToken cancellationToken = default);

/// <summary>
/// 2PC commit — prepare across all participants, then commit. Skips
/// the prepare phase for single-shard transactions.
/// </summary>
public Task CommitAsync(CancellationToken cancellationToken = default);

/// <summary>
/// Rolls back every enlisted participant.
/// </summary>
public Task RollbackAsync(CancellationToken cancellationToken = default);

public ITransactionParticipant? GetParticipant(string shardId);

public ValueTask DisposeAsync();
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
| `Timeout` | `TimeSpan` | 60s | Transaction timeout. Tx rolls back if it hasn't committed by then. |
| `IsolationLevel` | `CrossShardIsolationLevel` | `ReadCommitted` | Passed through to each participant's `BeginTransactionAsync(isolationLevel, ...)`. |
| `EnableRetry` | `bool` | `true` | Retry transient errors (deadlocks, timeouts, dropped connections) under `ExecuteInTransactionAsync`. |
| `MaxRetryAttempts` | `int` | `3` | Maximum retry attempts. |
| `RetryDelay` | `TimeSpan` | 100 ms | Initial delay. |
| `UseExponentialBackoff` | `bool` | `true` | Exponential backoff. |
| `MaxRetryDelay` | `TimeSpan` | 5 s | Maximum delay between retries. |
| `TransactionName` | `string?` | `null` | Tagged into the generated transaction id; useful for tracing. |
| `EnableRecovery` | `bool` | `false` | Persist lifecycle events via `ITransactionLog` so `coordinator.RecoverAsync` can drive in-doubt transactions to a terminal state. |

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

Represents a shard's participation in a cross-shard transaction. The
concrete class is `ShardTransactionParticipant` (in `Dtde.Core.Transactions`);
the public surface is exposed via `ITransactionParticipant`.

```csharp
namespace Dtde.Core.Transactions;

public sealed class ShardTransactionParticipant : ITransactionParticipant, IAsyncDisposable
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ShardId` | `string` | Shard identifier (fully-qualified `group::id` for named groups). |
| `Context` | `DbContext` | The per-shard DbContext. |
| `Vote` | `ParticipantVote` | The participant's current 2PC vote (`Pending`, `Prepared`, `ReadOnly`, `Abort`). |
| `HasPendingChanges` | `bool` | Whether the change tracker has unsaved work. |
| `PendingOperationCount` | `int` | Count of queued operations + (1 if HasPendingChanges else 0). |
| `SupportsSavepoints` | `bool` | True if the local provider supports savepoints (relational providers; false for in-memory). |

#### Methods

```csharp
/// <summary>
/// 2PC phase 1: SaveChangesAsync inside the open transaction, return a vote.
/// </summary>
public Task<ParticipantVote> PrepareAsync(CancellationToken cancellationToken = default);

/// <summary>
/// 2PC phase 2: commit the local transaction. Called for both Prepared
/// and ReadOnly participants — a ReadOnly participant may still hold work
/// from earlier SaveChangesAsync calls (e.g. inside bulk paths).
/// </summary>
public Task CommitAsync(CancellationToken cancellationToken = default);

/// <summary>
/// Roll back the local transaction. Idempotent; safe in dispose.
/// </summary>
public Task RollbackAsync(CancellationToken cancellationToken = default);

/// <summary>
/// Creates a named savepoint inside the local transaction. No-op for
/// providers that don't support savepoints.
/// </summary>
public Task CreateSavepointAsync(string savepointName, CancellationToken cancellationToken = default);

/// <summary>
/// Rolls the local transaction back to a previously-created savepoint.
/// The transaction stays open; only work after the savepoint is undone.
/// Clears the change tracker so subsequent reads see fresh state.
/// </summary>
public Task RollbackToSavepointAsync(string savepointName, CancellationToken cancellationToken = default);

/// <summary>
/// Releases a savepoint, discarding the ability to roll back to it.
/// </summary>
public Task ReleaseSavepointAsync(string savepointName, CancellationToken cancellationToken = default);
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

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Shards` | `IList<IShardMetadata>` | The flat shard list. |
| `ShardRegistry` | `IShardRegistry` | Flat shard registry. |
| `ShardGroupRegistry` | `IShardGroupRegistry` | Group-partitioned registry. |
| `MetadataRegistry` | `IMetadataRegistry` | Entity metadata registry. |
| `TemporalContext` | `ITemporalContext` | The "now" provider for temporal queries. |
| `DefaultTemporalContextProvider` | `Func<DateTime>?` | Overrides the default `DateTime.UtcNow`. |
| `MaxParallelShards` | `int` | Cap on parallel fan-out (default 10). |
| `EnableDiagnostics` | `bool` | Verbose routing logs. |
| `EnableTestMode` | `bool` | Single-shard fallback for tests. |

See [Configuration Reference](configuration.md) for the full set of
runtime knobs.

### ShardingBuilder\<T\>

The fluent return value of `ShardBy*` extensions on
`EntityTypeBuilder<T>`. Implicitly converts back to
`EntityTypeBuilder<T>` so `ShardBy*` can be the last call in
`OnModelCreating`.

```csharp
namespace Dtde.EntityFramework.Configuration;

public sealed class ShardingBuilder<TEntity> where TEntity : class
```

#### Methods

| Method | Description |
|--------|-------------|
| `WithStorageMode(ShardStorageMode)` | Override the entity's storage mode (`Tables` / `Databases` / `Manual`). |
| `WithTablePattern(string pattern)` | Customise the per-shard table name. Tokens: `{Table}`, `{Schema}`, `{ShardId}`. |
| `WithoutMigrations()` | Skip EF Core migrations for this entity (DBA-owned schema). |
| `UseShardGroup(string groupName)` | Bind this entity to a named shard group. |
| `Builder` (property) | The underlying `EntityTypeBuilder<TEntity>` — exposed for further EF-level configuration. |

### DtdeShardGroupBuilder

Group-scoped fluent builder, used inside `AddShardGroup(name, g => ...)`.
Every shard added through it is forced to belong to the enclosing group.

```csharp
namespace Dtde.EntityFramework.Configuration;

public sealed class DtdeShardGroupBuilder
```

#### Methods

| Method | Description |
|--------|-------------|
| `GroupName` (property) | The group this builder configures. |
| `AddShard(string id)` | Table-mode shard. |
| `AddShard(string id, string connectionString)` | Database-mode shard. |
| `AddTableShardInDatabase(string id, string connectionString)` | Mixed-mode shard. |
| `AddShards(params string[] ids)` | Bulk table-mode add. |
| `AddShard(Action<ShardMetadataBuilder> configure)` | Full fluent control; the group name is forced to this builder's group. |

### DtdeOptionsBuilder

Fluent builder for DTDE options.

```csharp
namespace Dtde.EntityFramework.Configuration;

public sealed class DtdeOptionsBuilder
```

#### Methods

| Method | Description |
|--------|-------------|
| `AddShard(string id)` | Table-mode shorthand: adds one shard to the default group. |
| `AddShard(string id, string connectionString)` | Database-mode shorthand: adds one shard with its own connection. |
| `AddTableShardInDatabase(string id, string connectionString)` | Mixed-mode: per-shard table inside a specific database. |
| `AddShards(params string[] ids)` | Bulk table-mode add. |
| `AddShard(Action<ShardMetadataBuilder>)` | Full fluent control: tier, priority, ranges, read-only. |
| `AddShardGroup(string name, Action<DtdeShardGroupBuilder>)` | Declares a named shard group. Entities bind via `UseShardGroup(name)`. |
| `AddShardsFromConfig(string)` | Loads shards from a JSON file. |
| `SetMaxParallelShards(int)` | Sets parallelism cap for fan-out queries. |
| `SetDefaultTemporalContext(Func<DateTime>)` | Overrides the default "now" used by `ValidAt`. |
| `EnableDiagnostics()` | Enables verbose routing/execution logs. |
| `EnableTestMode()` | Single-shard fallback for test environments. |

> Entity configuration (sharding, temporal) happens in
> `DbContext.OnModelCreating` via `EntityTypeBuilder<T>` extensions
> (`ShardBy`, `ShardByDate`, `ShardByHash`, `UseManualSharding`,
> `HasTemporalValidity`, etc.). There's no `ConfigureEntity<T>` on
> `DtdeOptionsBuilder`.

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
    Type ClrType { get; }
    string TableName { get; }
    string SchemaName { get; }
    IPropertyMetadata? PrimaryKey { get; }
    ITemporalConfiguration? TemporalConfiguration { get; }
    IShardingConfiguration? ShardingConfiguration { get; }
    bool IsTemporal { get; }
    bool IsSharded { get; }
}
```

### ITemporalConfiguration

```csharp
namespace Dtde.Abstractions.Temporal;

public interface ITemporalConfiguration
{
    IPropertyMetadata ValidFromProperty { get; }
    IPropertyMetadata? ValidToProperty { get; }
    bool IsOpenEnded { get; }
    DateTime OpenEndedValue { get; }

    Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(DateTime pointInTime);
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

    /// <summary>
    /// Streams results across shards as IAsyncEnumerable<TEntity>.
    /// Per-shard producers are concurrent into a bounded Channel<T>.
    /// Default buffer = shardCount * 64; minimum 16.
    /// </summary>
    IAsyncEnumerable<TEntity> ExecuteStreamingAsync<TEntity>(
        IQueryable<TEntity> query,
        int? bufferSize = null,
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

### IShardGroup, IShardGroupRegistry

```csharp
namespace Dtde.Abstractions.Metadata;

public interface IShardGroup
{
    string Name { get; }
    IReadOnlyList<IShardMetadata> Shards { get; }
    IShardMetadata? GetShard(string shardId);
}

public interface IShardGroupRegistry
{
    /// <summary>The conventional name of the default group: "__default__".</summary>
    public const string DefaultGroupName = "__default__";

    IShardGroup DefaultGroup { get; }
    IShardGroup? FindGroup(string name);
    IReadOnlyCollection<IShardGroup> Groups { get; }
}
```

Concrete impls: `ShardGroup` and `ShardGroupRegistry` in
`Dtde.Core.Metadata`. The registry is built automatically from
`DtdeOptionsBuilder.AddShardGroup(...)` and `AddShards(...)` calls.

### ITransactionLog

Durable log of cross-shard transaction lifecycle events. Used by
`coordinator.RecoverAsync()` to drive in-doubt transactions to a
terminal state after a coordinator crash.

```csharp
namespace Dtde.Abstractions.Transactions;

public interface ITransactionLog
{
    Task RecordTransactionStartedAsync(
        string transactionId,
        CrossShardTransactionOptions options,
        CancellationToken cancellationToken = default);

    Task RecordParticipantEnlistedAsync(
        string transactionId,
        string participantId,
        CancellationToken cancellationToken = default);

    Task RecordParticipantPreparedAsync(
        string transactionId,
        string participantId,
        CancellationToken cancellationToken = default);

    Task RecordTransactionCommittedAsync(
        string transactionId,
        CancellationToken cancellationToken = default);

    Task RecordTransactionRolledBackAsync(
        string transactionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TransactionLogEntry>> GetInDoubtTransactionsAsync(
        CancellationToken cancellationToken = default);
}
```

Shipped implementations (in `Dtde.Core.Transactions`):

- `InMemoryTransactionLog` — default. No persistence.
- `FileBasedTransactionLog` — JSON-lines append-only file. Survives
  process restarts; tolerant of corrupted trailing lines.

### IBulkInsertProvider

Pluggable per-provider bulk insert path.

```csharp
namespace Dtde.EntityFramework.Update;

public interface IBulkInsertProvider
{
    bool CanHandle(DbContext context);

    Task<int> BulkInsertAsync<TEntity>(
        DbContext context,
        IReadOnlyCollection<TEntity> entities,
        CancellationToken cancellationToken = default)
        where TEntity : class;
}
```

Shipped implementations:

- `DefaultBulkInsertProvider` — fallback. Always claims; uses
  `AddRangeAsync` + `SaveChangesAsync`.
- `BulkInsertProviderChain` — resolved by `BulkInsertAsync` to pick
  the first claiming provider; default sits at the tail.

---

## Enumerations

### ShardStorageMode

```csharp
public enum ShardStorageMode
{
    /// <summary>Multiple tables in the same database (per-shard tables).</summary>
    Tables,

    /// <summary>Separate database per shard.</summary>
    Databases,

    /// <summary>Pre-created tables; no migrations.</summary>
    Manual
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
