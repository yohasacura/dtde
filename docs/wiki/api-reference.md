# API reference

The public surface of `Dtde.EntityFramework`. For deeper extensibility
points (writing a custom sharding strategy, plugging in a transaction
log against a custom store), see the matching interfaces in
`Dtde.Abstractions`.

## DI registration

| API | Description |
|---|---|
| `services.AddDtdeDbContext<TContext>((db, conn) => ..., dtde => ...)` | The single canonical entry point. The first lambda configures the EF Core provider for both the parent context and each per-shard context (DTDE invokes it with `conn = null` for the parent and with the shard's connection for each per-shard context). The second lambda configures DTDE — shards, defaults, diagnostics. |
| `services.AddDtdeDbContext<TContext>(..., enableTransparentSharding: false)` | Same, but skips registering the `TransparentShardingInterceptor`, the cross-shard transaction coordinator, and the in-memory transaction log. Use when you want full manual control over cross-shard writes. |

## `DtdeOptionsBuilder` (the `dtde =>` lambda)

### Shards (default group)

| API | Description |
|---|---|
| `dtde.AddShard(string shardId)` | Adds a single table-mode shard to the default group. |
| `dtde.AddShard(string shardId, string connectionString)` | Adds a single database-mode shard. |
| `dtde.AddTableShardInDatabase(string shardId, string connectionString)` | Mixed-mode: per-shard tables hosted in a specific database (rather than the parent's default). |
| `dtde.AddShards(params string[] shardIds)` | Bulk table-mode add. |
| `dtde.AddShard(Action<ShardMetadataBuilder> configure)` | Full fluent control: tier, priority, read-only, date range, key range. |
| `dtde.AddShardsFromConfig(string jsonPath)` | Load shard definitions from a JSON file. |

### Shard groups

| API | Description |
|---|---|
| `dtde.AddShardGroup(string name, Action<DtdeShardGroupBuilder> configure)` | Declares a named group. The `configure` callback exposes `AddShard`, `AddShards`, `AddTableShardInDatabase` scoped to the group. Required when different entities have different shard topologies. |

### Runtime options

| API | Description |
|---|---|
| `dtde.SetDefaultTemporalContext(Func<DateTime>)` | Override the default `DateTime.UtcNow` clock for `ValidAt`. |
| `dtde.SetMaxParallelShards(int)` | Cap on parallel shard queries during fan-out (default 10). |
| `dtde.EnableDiagnostics()` | Verbose routing/execution logs. |
| `dtde.EnableTestMode()` | Single-shard fallback (no fan-out). For test environments only. |

## `DtdeDbContext` extensions

### Lifecycle

| API | Description |
|---|---|
| `db.EnsureAllShardsCreatedAsync(CancellationToken)` | Creates the parent's tables, then walks every shard group and creates each shard's tables/database. |

### Cross-shard transactions

| API | Description |
|---|---|
| `db.BeginCrossShardTransactionAsync(CancellationToken)` | Begin with default options. |
| `db.BeginCrossShardTransactionAsync(CrossShardTransactionOptions, CancellationToken)` | Begin with explicit options (isolation level, timeout, retry, recovery). |

The returned `ICrossShardTransaction` is `IAsyncDisposable` — wrap in
`await using` to ensure rollback on uncommitted exit.

### Bulk operations

| API | Description |
|---|---|
| `db.BulkInsertAsync<TEntity>(IEnumerable<TEntity>, CancellationToken)` | Routes per shard, batches per shard, dispatches through the registered `IBulkInsertProvider` chain. Inside an ambient transaction, routes through the participant. |
| `db.BulkUpdateAsync<TEntity>(filter, setters, CancellationToken)` | Set-based UPDATE fan-out. **EF 7-9 / EF 10 signatures differ** — picked at compile time via `#if NET10_0_OR_GREATER`. |
| `db.BulkDeleteAsync<TEntity>(filter, CancellationToken)` | Set-based DELETE fan-out. |

### Temporal queries

| API | Description |
|---|---|
| `db.ValidAt<TEntity>(DateTime asOfDate)` | Queryable filtered to entities valid at the given moment. |
| `db.ValidBetween<TEntity>(DateTime start, DateTime end)` | Queryable filtered to entities valid in any subset of `[start, end]`. |
| `db.AllVersions<TEntity>()` | Queryable bypassing temporal filtering — every version. |
| `db.CreateNewVersion<TEntity>(currentEntity, changes, effectiveDate)` | Creates a new version of an entity, terminating the current one as of `effectiveDate.AddTicks(-1)`. |
| `db.Terminate<TEntity>(entity, terminationDate)` | Closes off an entity's validity. |
| `db.AddTemporal<TEntity>(entity, effectiveFrom)` | Adds a new temporal entity with `ValidFrom` initialised. |

## `EntityTypeBuilder<T>` extensions (in `OnModelCreating`)

### Sharding

| API | Description |
|---|---|
| `entity.ShardBy(c => c.Property)` | Property-value sharding. Returns `ShardingBuilder<T>` for chaining. |
| `entity.ShardByDate(c => c.DateProperty, DateShardInterval)` | Time-bucketed sharding. |
| `entity.ShardByHash(c => c.Property, shardCount: N)` | Hash-modulo sharding. |
| `entity.UseManualSharding(config => ...)` | Pre-existing tables (DBA-owned schema). |

### `ShardingBuilder<T>` chained options

| API | Description |
|---|---|
| `.WithStorageMode(ShardStorageMode)` | Override the entity's storage mode. |
| `.WithTablePattern("{Table}_{ShardId}")` | Customise the per-shard table name. Tokens: `{Table}`, `{Schema}`, `{ShardId}`. |
| `.WithoutMigrations()` | Skip EF Core migrations for this entity. |
| `.UseShardGroup("name")` | Bind the entity to a named shard group. |

### Temporal

| API | Description |
|---|---|
| `entity.HasTemporalValidity(c => c.ValidFrom, c => c.ValidTo)` | Standard `(start, end)` validity. |
| `entity.HasTemporalValidity(c => c.ValidFrom)` | Open-ended (no end date). |
| `entity.HasTemporalContainment(TemporalContainmentRule)` | Parent-child validity rule. |

## Cross-shard transaction types

| Type | Notes |
|---|---|
| `CrossShardTransactionOptions` | Configuration. Static presets: `.Default`, `.ShortLived`, `.LongRunning`. |
| `CrossShardIsolationLevel` | `ReadCommitted`, `RepeatableRead`, `Serializable`, `Snapshot`. |
| `ICrossShardTransaction` | Public interface; `IAsyncDisposable`. `EnlistAsync`, `CommitAsync`, `RollbackAsync`, `GetParticipant`, `EnlistedShards`. |
| `CrossShardTransaction` | Concrete impl; cast to it to call `GetOrCreateParticipantAsync(IShardMetadata or string)`. |
| `ITransactionParticipant` | Per-shard participant. `Context`, `PrepareAsync`, `CommitAsync`, `RollbackAsync`, `CreateSavepointAsync`, `RollbackToSavepointAsync`, `ReleaseSavepointAsync`, `SupportsSavepoints`. |
| `ICrossShardTransactionCoordinator` | Service. `BeginTransactionAsync`, `ExecuteInTransactionAsync<TResult>`, `CurrentTransaction`, `RecoverAsync`. |

## Transaction log types

| Type | Notes |
|---|---|
| `ITransactionLog` | The contract. Six methods: record start, enlisted, prepared, committed, rolled-back; query in-doubt. |
| `InMemoryTransactionLog` | Default. No persistence. |
| `FileBasedTransactionLog` | JSON-lines append-only file. `IDisposable`. Tolerant of corrupted final lines. |
| `TransactionLogEntry` | Returned by `GetInDoubtTransactionsAsync` — id, started-at, enlisted/prepared participant lists, latest known state. |

## Bulk insert provider types

| Type | Notes |
|---|---|
| `IBulkInsertProvider` | The contract. `CanHandle(DbContext)`, `BulkInsertAsync<TEntity>(...)`. |
| `DefaultBulkInsertProvider` | The fallback. Always claims; uses `AddRangeAsync` + `SaveChangesAsync`. |
| `BulkInsertProviderChain` | Resolves providers in DI order with the default last. |

## Streaming

| API | Description |
|---|---|
| `IShardedQueryExecutor.ExecuteStreamingAsync<TEntity>(IQueryable<TEntity>, int? bufferSize, CancellationToken)` | Streams results as `IAsyncEnumerable<TEntity>`. Bounded buffer; per-shard producers are concurrent; abandoning the stream tears them down. |

## See also

- [Configuration](configuration.md) — JSON shard config schema, full
  `DtdeOptionsBuilder` reference.
- [Architecture](architecture.md) — internal layering and request flows.
- [Troubleshooting](troubleshooting.md) — common errors and fixes.
