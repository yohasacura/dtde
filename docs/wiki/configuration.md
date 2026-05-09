# Configuration

Every option you can set on `DtdeOptionsBuilder` (the `dtde =>`
callback in `AddDtdeDbContext`), plus the JSON shard-config file
schema.

## DI registration

```csharp
services.AddDtdeDbContext<AppDbContext>(
    (db, conn) => db.UseSqlite(conn ?? "Data Source=app.db"),
    dtde => dtde
        .AddShards("EU", "US", "APAC")
        .SetMaxParallelShards(8)
        .EnableDiagnostics());
```

Two callbacks:

1. **`(db, conn) => ...`** — configures the EF Core provider. DTDE
   invokes it with `conn = null` for the parent context (you typically
   fall back to a default connection string) and with the shard's
   connection for each per-shard context (database-mode and mixed-mode
   shards).
2. **`dtde => ...`** — configures DTDE itself.

Optional third argument `enableTransparentSharding: false` opts out of
the auto-promotion interceptor and the cross-shard transaction
coordinator. Use only if you need full manual control.

## Shard registration

### Default group (single-topology applications)

```csharp
// Bulk shorthand for table-mode shards.
dtde.AddShards("EU", "US", "APAC");

// Single-shard variants.
dtde.AddShard("EU");                                          // table-mode
dtde.AddShard("EU", "Server=eu.db;...");                     // database-mode
dtde.AddTableShardInDatabase("EU", "Server=primary.db;..."); // mixed-mode

// Full fluent control.
dtde.AddShard(s => s
    .WithId("2024-archive")
    .WithName("2024 archive")
    .WithConnectionString(archiveConnectionString)
    .WithTier(ShardTier.Cold)
    .WithDateRange(new DateTime(2024, 1, 1), new DateTime(2025, 1, 1))
    .AsReadOnly());
```

### Named groups (multi-topology applications)

```csharp
dtde
    .AddShardGroup("hash8", g => g.AddShards("0","1","2","3","4","5","6","7"))
    .AddShardGroup("years", g => g.AddShards("2023","2024","2025"))
    // The default group is still available alongside.
    .AddShards("EU", "US", "APAC");
```

`DtdeShardGroupBuilder` exposes the same shorthand as the outer
builder — `AddShard(id)`, `AddShard(id, conn)`,
`AddTableShardInDatabase(id, conn)`, `AddShards(params string[])`,
plus `AddShard(Action<ShardMetadataBuilder>)` for full fluent control —
but locked to the group.

### `ShardMetadataBuilder` reference

| Method | Purpose |
|---|---|
| `WithId(string)` | The shard's id (unique within its group). Required. |
| `WithGroup(string)` | The shard's group name. Defaults to the default group. |
| `WithName(string)` | Display name for diagnostics. |
| `WithStorageMode(ShardStorageMode)` | `Tables` / `Databases` / `Manual`. Defaults to `Tables`. |
| `WithTable(string tableName, string schemaName = "dbo")` | Sets the per-shard table (manual / table-mode). |
| `WithConnectionString(string)` | The per-shard connection (database-mode and mixed-mode). |
| `WithShardKeyValue(string)` | The shard-key value this shard handles (used by property-value strategy). Defaults to the shard id. |
| `WithDateRange(DateTime, DateTime)` | The date range this shard covers (date-range sharding). |
| `WithKeyRange(KeyRange)` | The key range this shard covers (range sharding). |
| `WithTier(ShardTier)` | `Hot` / `Warm` / `Cold` / `Archive`. Defaults to `Hot`. |
| `AsReadOnly()` | Marks the shard read-only (no writes will route to it). |
| `WithPriority(int)` | Lower wins when multiple shards match. Defaults to 100. |

## Runtime options

```csharp
dtde
    .SetDefaultTemporalContext(() => DateTime.UtcNow)
    .SetMaxParallelShards(10)
    .EnableDiagnostics();
```

| Option | Default | Notes |
|---|---|---|
| `SetDefaultTemporalContext(Func<DateTime>)` | `DateTime.UtcNow` | The "now" used by `ValidAt` when no point-in-time is supplied. |
| `SetMaxParallelShards(int)` | 10 | Cap on parallel per-shard query tasks during fan-out. |
| `EnableDiagnostics()` | off | Verbose routing/execution logs. |
| `EnableTestMode()` | off | Single-shard fallback. For test environments only. |

## Cross-shard transaction options

`CrossShardTransactionOptions` is a regular class — instantiate it to
override defaults per call. See [cross-shard transactions](../guides/cross-shard-transactions.md)
for full coverage.

| Property | Default | Notes |
|---|---|---|
| `IsolationLevel` | `ReadCommitted` | Passed through to participants. |
| `Timeout` | 60 s | Tx times out and rolls back. |
| `EnableRetry` | `true` | Retry on transient errors via `ExecuteInTransactionAsync`. |
| `MaxRetryAttempts` | 3 | |
| `RetryDelay` | 100 ms | Exponential backoff up to `MaxRetryDelay`. |
| `MaxRetryDelay` | 5 s | |
| `UseExponentialBackoff` | `true` | |
| `TransactionName` | none | Tagged into the generated transaction id. |
| `EnableRecovery` | `false` | Persist lifecycle events via `ITransactionLog`. |

Two presets: `CrossShardTransactionOptions.ShortLived` (10 s, 2
retries) and `LongRunning` (5 min, 5 retries, recovery on).

## Transaction log

```csharp
// File-backed durable log for crash recovery.
services.AddSingleton<ITransactionLog>(_ =>
    new FileBasedTransactionLog("/var/dtde/tx-log.jsonl"));

services.AddDtdeDbContext<AppDbContext>(...);
// The log is auto-wired into the coordinator on registration.
```

The default is `InMemoryTransactionLog`. See
[transaction log and recovery](../guides/transaction-log-and-recovery.md).

## Bulk insert providers

```csharp
services.AddSingleton<IBulkInsertProvider, MyProviderSpecificBulkInsert>();
services.AddDtdeDbContext<AppDbContext>(...);
```

DI registration order matters — registrations BEFORE
`AddDtdeDbContext` go ahead of the default; DTDE picks the first
provider whose `CanHandle(context)` returns `true`. See
[bulk operations](../guides/bulk-operations.md).

## JSON shard configuration

`AddShardsFromConfig("shards.json")` loads shards from a JSON file:

```json
{
  "shards": [
    {
      "shardId": "EU",
      "name": "EU primary",
      "connectionString": "Server=eu.db;Database=Customers;...",
      "tier": "Hot",
      "priority": 1,
      "dateRangeStart": null,
      "dateRangeEnd": null,
      "isReadOnly": false
    },
    {
      "shardId": "US",
      "name": "US primary",
      "connectionString": "Server=us.db;Database=Customers;...",
      "tier": "Hot",
      "priority": 1
    },
    {
      "shardId": "2024-archive",
      "name": "2024 archive",
      "connectionString": "Server=archive.db;Database=Archive2024;...",
      "tier": "Cold",
      "priority": 100,
      "dateRangeStart": "2024-01-01",
      "dateRangeEnd": "2025-01-01",
      "isReadOnly": true
    }
  ]
}
```

| Field | Type | Notes |
|---|---|---|
| `shardId` | string | Required. |
| `name` | string | Optional; defaults to `shardId`. |
| `connectionString` | string | Required for database-mode / mixed-mode. |
| `tier` | string | `Hot` / `Warm` / `Cold` / `Archive`. Defaults to `Hot`. |
| `priority` | int | Lower wins. Defaults to 100. |
| `dateRangeStart` / `dateRangeEnd` | ISO-8601 datetime | Optional. |
| `isReadOnly` | bool | Defaults to `false`. |

The JSON loader currently doesn't support shard groups directly — for
group-based topologies, use the fluent API.

## Configuration patterns

### Per-environment connection strings

```csharp
builder.Services.AddDtdeDbContext<AppDbContext>(
    (db, conn) => db.UseSqlServer(conn ?? builder.Configuration.GetConnectionString("Default")),
    dtde => dtde
        .AddShard("EU", builder.Configuration.GetConnectionString("EU")!)
        .AddShard("US", builder.Configuration.GetConnectionString("US")!));
```

### Test-only single-shard fallback

```csharp
dtde => dtde
    .AddShard("test")
    .EnableTestMode();
```

### Verbose tracing

```csharp
dtde => dtde
    .AddShards("EU", "US", "APAC")
    .EnableDiagnostics();
```

## See also

- [Architecture](architecture.md)
- [API reference](api-reference.md)
- [Troubleshooting](troubleshooting.md)
