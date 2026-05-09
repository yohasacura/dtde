# Cross-shard transactions

DTDE coordinates writes across multiple shards with a two-phase commit
(2PC). The public surface is small and consistent regardless of how many
shards a transaction touches. This guide is the canonical reference for
everything transaction-related in DTDE.

## The mental model

Every per-shard `DbContext` has its own local database transaction. A
**cross-shard transaction** is a coordinator that drives a 2PC across
those local transactions:

1. **Prepare** — every participant's local transaction is told to
   "validate and lock, but don't commit yet". On a relational provider
   that's a `SaveChangesAsync` inside the open transaction.
2. **Commit** — once *every* participant has voted to commit, each
   local transaction is committed.

If any participant aborts during prepare, every other participant rolls
back. The standard 2PC failure-mode rules apply: if the coordinator dies
between prepare and commit, every fully-prepared transaction is
*durable* — recovery (see below) will commit them.

DTDE optimises the single-shard case automatically: a cross-shard
transaction with exactly one enlisted participant skips the prepare
phase. There's no 2PC overhead when only one shard is touched.

## The simple way: auto-promotion via `SaveChangesAsync`

If you write to two shards through the parent `DbContext` and call
`SaveChangesAsync()`, DTDE detects that and automatically wraps the
write in a cross-shard transaction. **You don't have to do anything.**

```csharp
// AppDbContext is sharded by Region.
db.Customers.Add(new Customer { Region = "EU", ... });
db.Customers.Add(new Customer { Region = "US", ... });

await db.SaveChangesAsync();  // 2PC commit across EU and US.
```

This is enough for most application code. The rest of this guide is
about the **explicit** API for cases where you need finer control.

## Explicit transactions: `BeginCrossShardTransactionAsync`

```csharp
await using var tx = await db.BeginCrossShardTransactionAsync(
    new CrossShardTransactionOptions
    {
        IsolationLevel = CrossShardIsolationLevel.Serializable,
        Timeout = TimeSpan.FromSeconds(15),
    });

var crossShardTx = (CrossShardTransaction)tx;

// Get-or-create a participant per shard. The first call enlists the
// shard; subsequent calls return the same participant.
var fromShard = db.ShardRegistry.GetShard("EU")!;
var toShard   = db.ShardRegistry.GetShard("US")!;

var fromP = await crossShardTx.GetOrCreateParticipantAsync(fromShard);
var toP   = await crossShardTx.GetOrCreateParticipantAsync(toShard);

// Each participant has its own DbContext scoped to that shard.
fromP.Context.Set<Account>().Update(sender);
toP.Context.Set<Account>().Update(receiver);

await tx.CommitAsync();
```

`await using` guarantees the transaction is rolled back if you don't
commit. `RollbackAsync()` is also explicit and idempotent.

The full options on `CrossShardTransactionOptions`:

| Property | Default | Notes |
|---|---|---|
| `IsolationLevel` | `ReadCommitted` | Passed through to each participant's `BeginTransactionAsync(isolationLevel, ...)`. Relational providers honour it; in-memory ignores it. |
| `Timeout` | 60s | Tx times out and rolls back if it hasn't committed by then. |
| `EnableRetry` | `true` | Retries transient errors (deadlocks, timeouts, dropped connections) automatically when using `ExecuteInTransactionAsync`. |
| `MaxRetryAttempts` | 3 | |
| `RetryDelay` | 100 ms (with exponential backoff up to `MaxRetryDelay`) | |
| `TransactionName` | none | Tagged into the generated transaction id; useful for tracing. |
| `EnableRecovery` | `false` | When `true`, lifecycle events are persisted via `ITransactionLog` so `RecoverAsync` can drive in-doubt transactions to a terminal state after a crash. |

Two shorthand presets ship in the box: `CrossShardTransactionOptions.ShortLived`
(10s timeout, 2 retries) and `LongRunning` (5 min, 5 retries, recovery
enabled).

## Read-after-write inside a transaction

Queries inside the scope of `BeginCrossShardTransactionAsync` reuse each
shard's open participant context. That means writes earlier in the
transaction are visible to subsequent reads on the same shard:

```csharp
await using var tx = await db.BeginCrossShardTransactionAsync();
var crossShardTx = (CrossShardTransaction)tx;
var participant = await crossShardTx.GetOrCreateParticipantAsync(euShard);

participant.Context.Set<Account>().Add(new Account { Id = 1, Region = "EU", Balance = 100 });
await participant.Context.SaveChangesAsync();

// This query is run via the executor — it sees the uncommitted insert
// because the executor reuses the same participant context.
var euTotal = await executor.ExecuteAsync(
    db.Set<Account>().Where(a => a.Region == "EU").AsQueryable());
// euTotal contains the new row.
```

Shards not yet enlisted are auto-enlisted at first touch, so the
entire scope inside `BeginCrossShardTransactionAsync` is transactional.

## Savepoints: within-shard partial rollback

Savepoints let you "try and fall back" inside a long-running
transaction without rolling the whole thing back:

```csharp
await participant.CreateSavepointAsync("bonus");

try
{
    await TryApplyOptionalBonus(participant.Context);
}
catch (BonusNotEligibleException)
{
    await participant.RollbackToSavepointAsync("bonus");
    // The transaction stays open. Anything earlier persists. Only the
    // bonus work was undone.
}
```

Optional `ReleaseSavepointAsync(name)` discards the savepoint to free
server-side resources on long-running transactions; savepoints are also
discarded automatically on commit/rollback.

Savepoints are a relational-provider feature. On non-relational
providers (in-memory) the calls are no-ops; check
`participant.SupportsSavepoints` if you need to vary behaviour.

## Bulk operations inside a transaction

`BulkInsertAsync`, `BulkUpdateAsync`, and `BulkDeleteAsync` participate
automatically when an ambient transaction is active:

```csharp
await using var tx = await db.BeginCrossShardTransactionAsync();

await db.BulkInsertAsync(largeEntityBatch);          // routes per shard
                                                      // through the same tx
await db.BulkUpdateAsync<Event>(e => e.Type == "old",
    setters => setters.SetProperty(e => e.Type, "new"));

await tx.CommitAsync();
// Or RollbackAsync — every per-shard write is undone.
```

Outside an ambient transaction, the bulk APIs use the single-shard fast
path or open their own short-lived 2PC, depending on whether one or more
shards are touched.

## Crash recovery: `ITransactionLog` + `RecoverAsync`

Production deployments need durability — if the coordinator process
crashes between prepare and commit, the in-doubt transactions must
either be driven to commit (if every participant prepared) or rolled
back (otherwise).

DTDE ships two `ITransactionLog` implementations:

| Impl | Persistence | Use |
|---|---|---|
| `InMemoryTransactionLog` (default) | in-process | Development, single-process tests. |
| `FileBasedTransactionLog` | JSON-lines append-only file | Single-node deployments, integration tests. Survives restarts; tolerant of corrupted final lines from a mid-write crash. |

Plug in your own (Postgres, Redis, etc.) for multi-coordinator
production:

```csharp
services.AddSingleton<ITransactionLog>(_ =>
    new FileBasedTransactionLog("/var/dtde/tx-log.jsonl"));

services.AddDtdeDbContext<AppDbContext>(...);   // log is auto-wired into the coordinator
```

On startup:

```csharp
using var scope = app.Services.CreateScope();
var coordinator = scope.ServiceProvider.GetRequiredService<ICrossShardTransactionCoordinator>();
var resolved = await coordinator.RecoverAsync();
logger.LogInformation("Resolved {Count} in-doubt transactions on startup.", resolved);
```

`RecoverAsync` applies the classical 2PC recovery rule:

- **Every enlisted participant logged a `prepared` vote**
  → resolve as **committed**. The global decision was already made; the
  participants' local transactions either auto-commit on reconnect (some
  drivers) or are picked up by external monitoring.
- **Some participants never logged `prepared`**
  → resolve as **rolled back**. The decision is recorded so the log no
  longer flags the transaction as in-doubt; any orphaned local
  transactions are cleaned up by the relational provider when the
  connection drops.

The log is intentionally append-only and JSON-lines so external tooling
(observability, audit) can consume it directly. There's no rotation;
once the log is full of completed transactions, the operator can safely
truncate it (only `Started` entries are inspected by recovery).

## Diagnostics and tracing

The coordinator and per-shard participants emit structured logs (event
IDs `10001-10199`). Key events:

| Event | Meaning |
|---|---|
| `BeginningTransactionWithOptions` | New transaction started. |
| `EnlistedShard` | A participant joined. |
| `PreparePhaseCompleted` | All participants voted `prepared`. |
| `TransactionCommitted` / `TransactionRolledBack` | Terminal events. |
| `RecoveringInDoubtTransactions` | Recovery scan starting. |
| `RecoveredCommittedTransaction` / `RecoveredRolledBackTransaction` | Resolution decisions. |

Couple them with your standard structured-logging pipeline (Seq,
Datadog, etc.) for production observability.

## What DTDE does *not* do (yet)

- **Distributed deadlock detection.** A long-running transaction
  touching shards A and B may deadlock against another transaction
  touching B and A. DTDE relies on each provider's own deadlock
  detection plus its retry policy.
- **Heuristic resolution.** If a participant's local transaction is
  manually resolved out-of-band (DBA action), the log will show "in
  doubt" indefinitely — `RecoverAsync` makes the global decision but
  doesn't roll forward heuristic completions on the participant.
- **Cross-group transactions.** Writes inside a single
  `BeginCrossShardTransactionAsync` scope can hit shards from multiple
  groups, but every shard must already be registered. There's no
  cross-group migration helper today.

## See also

- The runnable [`Dtde.Samples.Transactions`](https://github.com/yohasacura/dtde/tree/main/samples/Dtde.Samples.Transactions) project — every section above corresponds to one endpoint.
- The [bulk operations guide](bulk-operations.md) — how `BulkInsert/Update/Delete` interact with transactions.
- The [sharding guide](sharding-guide.md) — how shard groups affect transaction routing.
