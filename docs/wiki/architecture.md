# Architecture

How DTDE is laid out internally — the three projects, the key
abstractions, and how a query / write / transaction flows through the
system.

## Three projects, strict layering

```
┌────────────────────────────────────────────────────────┐
│  Dtde.EntityFramework  ←  application code references  │
│   (EF Core integration, DbContext, query rewriter,     │
│    interceptors, bulk extensions)                       │
└────────────────────┬───────────────────────────────────┘
                     │
┌────────────────────▼───────────────────────────────────┐
│  Dtde.Core                                              │
│   (sharding strategies, temporal context, cross-shard   │
│    transaction coordinator, transaction log impls)      │
└────────────────────┬───────────────────────────────────┘
                     │
┌────────────────────▼───────────────────────────────────┐
│  Dtde.Abstractions                                      │
│   (public interfaces — IShardMetadata,                  │
│    IShardingStrategy, ICrossShardTransactionCoordinator,│
│    ITransactionLog, IBulkInsertProvider, ...)           │
└────────────────────────────────────────────────────────┘
```

| Layer | Depends on | Purpose |
|---|---|---|
| `Dtde.Abstractions` | `Microsoft.Extensions.Logging.Abstractions` only | Public contract surface. Extension points for custom providers. |
| `Dtde.Core` | `Dtde.Abstractions`, `Microsoft.EntityFrameworkCore` (no relational provider) | Default implementations of the abstractions. |
| `Dtde.EntityFramework` | `Dtde.Core`, `Microsoft.EntityFrameworkCore.Relational` | EF Core integration: `DtdeDbContext`, model customizers, interceptors, public extension methods. |

Application developers reference **`Dtde.EntityFramework`** only.

## Key abstractions

### Sharding

| Type | What it does |
|---|---|
| `IShardMetadata` | Describes one physical shard — id, group, storage mode, connection string, table name pattern, tier, priority. |
| `IShardGroup` | Named set of shards. Entities bind to a group via `UseShardGroup(name)`. |
| `IShardGroupRegistry` | Top-level registry of groups. The default group is always present; `dtde.AddShards(...)` populates it. |
| `IShardRegistry` | Flat union view across groups, keyed by fully-qualified id (`group::id` for named groups; just `id` for default-group shards). |
| `IShardingStrategy` | Resolves a shard for a write or a query predicate. Three default impls: `PropertyBasedShardingStrategy`, `HashShardingStrategy(N)`, `DateRangeShardingStrategy`. |
| `IShardingConfiguration` | Per-entity sharding configuration: strategy, key properties, storage mode, group binding. |

### Temporal

| Type | What it does |
|---|---|
| `ITemporalConfiguration` | Per-entity validity-property configuration (`ValidFrom` / optional `ValidTo`). |
| `ITemporalContext` | The `now` provider used by `ValidAt`, etc. Defaults to `DateTime.UtcNow`. |

### Transactions

| Type | What it does |
|---|---|
| `ICrossShardTransactionCoordinator` | Begins, executes, recovers cross-shard transactions. Holds the `AsyncLocal<ICrossShardTransaction>` used by `CurrentTransaction`. |
| `ICrossShardTransaction` | A single 2PC scope. `EnlistAsync`, `CommitAsync`, `RollbackAsync`, `GetParticipant`. |
| `ITransactionParticipant` | One shard's view of a cross-shard transaction. Owns its own local transaction; supports `PrepareAsync`, `CommitAsync`, `RollbackAsync`, `CreateSavepointAsync`, `RollbackToSavepointAsync`, `ReleaseSavepointAsync`. |
| `ITransactionLog` | Durable record of lifecycle events for crash recovery. Two shipped impls: `InMemoryTransactionLog`, `FileBasedTransactionLog`. Plug in your own for production. |

### Bulk operations

| Type | What it does |
|---|---|
| `IBulkInsertProvider` | Pluggable per-provider bulk insert. Default implementation uses `AddRangeAsync` + `SaveChangesAsync`. |
| `BulkInsertProviderChain` | Resolves the providers in DI registration order with the default at the tail. |

## How a query flows

```
db.Customers.Where(c => c.Region == "EU").ToListAsync()
                       │
                       ▼
        DtdeExpressionRewriter (optional rewrite for temporal filters, etc.)
                       │
                       ▼
        ShardedQueryExecutor.ExecuteAsync(query)
                       │
                       ▼
        DetermineTargetShards(typeof(Customer), expression)
            ├── Look up entity's IShardingConfiguration → group name
            ├── Look up the group → IShardGroup
            ├── Predicate-prune via the group's strategy
            └── Return the matching IShardMetadata list
                       │
                       ▼
        For each shard:
            GetContextForShardAsync(shard)
                ├── If an ambient cross-shard transaction is active
                │   AND has a participant for this shard → reuse its context
                │   (read-after-write); otherwise auto-enlist + reuse.
                └── Else → IShardContextFactory.CreateContextAsync(shard)
                       │
                       ▼
            Apply expression to the per-shard DbSet, ToListAsync().
                       │
                       ▼
        Merge results, apply paging / ordering, return.
```

`ExecuteStreamingAsync<T>` follows the same shape but runs the per-shard
producers concurrently into a bounded `Channel<T>` and yields entities
in arrival order via `IAsyncEnumerable<T>` — see
[bulk operations](../guides/bulk-operations.md).

## How a write flows

```
db.Customers.Add(...) + db.SaveChangesAsync()
                       │
                       ▼
        TransparentShardingInterceptor.SavingChangesAsync
                       │
                       ├── No sharded changes? → let EF's normal SaveChanges proceed.
                       │
                       └── Cross-shard? → group entries by target shard
                                          (using ShardWriteRouter.DetermineTargetShard,
                                          keyed by ToQualifiedId() for safety),
                                          then run each group inside a
                                          CrossShardTransaction.
```

`BulkInsertAsync` / `BulkUpdateAsync` / `BulkDeleteAsync` follow the
same routing logic but bypass the change tracker — they call directly
into `IBulkInsertProvider` (insert) or fan out
`ExecuteUpdate/DeleteAsync` (update/delete).

## How a cross-shard transaction flows

```
db.BeginCrossShardTransactionAsync(options)
                       │
                       ▼
        CrossShardTransactionCoordinator.BeginTransactionAsync
            ├── Generate transaction id.
            ├── Construct CrossShardTransaction (with onDisposed callback
            │   that clears the AsyncLocal slot).
            ├── _currentTransaction.Value = transaction (synchronous).
            ├── Record "Started" in the ITransactionLog (async).
            └── Return the transaction.
                       │
                       ▼
        User-driven enlistments + writes via participant.Context
                       │
                       ▼
        CommitAsync()
            ├── Single participant?  → single-shard fast path.
            │                           PrepareAsync + CommitAsync on the participant.
            │                           No 2PC overhead.
            │
            └── Multiple participants?
                  ├── Phase 1 (Prepare):
                  │      For each participant: SaveChanges inside its open transaction;
                  │      record "Prepared" in the durable log.
                  ├── Phase 2 (Commit):
                  │      For each participant: commit its local transaction.
                  └── Record "Committed" in the durable log.
                       │
                       ▼
        DisposeAsync() — callback clears the AsyncLocal slot.
```

If the coordinator process dies after Phase 1 but before Phase 2, the
durable log still has all `Prepared` votes. On restart,
`coordinator.RecoverAsync()` finds those in-doubt transactions and
applies the classical 2PC rule: every participant prepared → resolve
as committed; otherwise resolve as rolled back.

## Per-shard model materialisation

Each per-shard `DbContext` has its own EF Core model — different tables
(in table-mode) or different entity sets (in mixed-group setups). DTDE
materialises these models via:

1. **`DtdeOptionsExtension.WithActiveShard(shard)`** — clones the
   options extension and tags it with the active shard.
2. **`DtdeModelCacheKeyFactory`** — builds a cache key from
   `(ContextType, ActiveShardGroup, ActiveShardId, StorageMode, DesignTime)`.
   Two shards with the same local id in different groups produce
   different models; the cache keeps them isolated.
3. **`DtdeShardModelCustomizer`** — runs after the user's
   `OnModelCreating`. For per-shard contexts:
    - rewrites table names per the entity's pattern (`{Table}_{ShardId}`
      by default);
    - excludes out-of-group entities from the model so
      `EnsureCreatedAsync`/`CreateTablesAsync` only provisions tables
      that actually live on that shard.
    - For the parent context, it validates that every entity's
      declared shard group is registered, throwing immediately at
      first-model-build time if not.

The `PerShardContextFactory<TContext>` brings it all together: takes an
`IShardMetadata`, clones the options with `WithActiveShard(shard)`,
calls the user-supplied `(db, conn) => db.UseSqlite(...)` callback
with the shard's connection string, and `Activator.CreateInstance`s
the user's `DbContext` subclass.

## Source-link / determinism / signing

CI builds set `CI=true`, which activates:

- **Source Link** — debug symbols point back to the GitHub source.
- **Deterministic builds** — bit-identical output for the same inputs.
- **`PublicAPI.Shipped.txt` / `Unshipped.txt`** tracking — every public
  symbol is listed; new ones must be added to `Unshipped.txt` first.
- **Banned-API analyzer** — `String.GetHashCode`, `DateTime.Now`,
  `Thread.Sleep`, etc. are forbidden. Sharding identity must be
  deterministic; time must be UTC; blocking patterns are out.

## See also

- [API reference](api-reference.md) — public type catalogue.
- [Configuration](configuration.md) — every option on `DtdeOptionsBuilder`.
- [Sharding guide](../guides/sharding-guide.md) — strategies, storage modes, groups.
- [Cross-shard transactions](../guides/cross-shard-transactions.md) — full transaction lifecycle.
