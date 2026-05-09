# Transaction log and recovery

DTDE's cross-shard transactions are durable: every lifecycle event
(start, enlist, prepare, commit, rollback) is written to a pluggable
`ITransactionLog` so a coordinator restart can drive in-doubt
transactions to a terminal state.

## Why you need this

Two-phase commit has a well-known weakness: if the coordinator dies
between **prepare** and **commit**, every participant has voted
"prepared" but doesn't know what the final decision was. They'll hold
locks until somebody resolves the situation.

The fix is the classical 2PC recovery rule:

- **All participants logged `prepared`** → resolve as committed (the
  global decision was already made before the crash; the participants
  must finish committing).
- **At least one participant didn't reach `prepared`** → resolve as
  rolled back (the global decision wasn't yet made, so abort).

`ITransactionLog` is the durable record that lets the coordinator make
that decision after a restart.

## Shipped implementations

### `InMemoryTransactionLog` — default

Records lifecycle events in a process-local `ConcurrentDictionary`. No
persistence; no recovery across restarts. The default registration when
you call `AddDtdeDbContext`.

Use for: development, single-process tests, environments where you've
explicitly decided you don't need crash recovery.

### `FileBasedTransactionLog` — persistent

JSON-lines append-only file. Each lifecycle event is one line:

```jsonl
{"transactionId":"XS-20251201T120000-abc12345","eventType":"Started","timestamp":"...","isolationLevel":"ReadCommitted"}
{"transactionId":"XS-...","eventType":"Enlisted","timestamp":"...","participantId":"EU"}
{"transactionId":"XS-...","eventType":"Prepared","timestamp":"...","participantId":"EU"}
{"transactionId":"XS-...","eventType":"Committed","timestamp":"..."}
```

The file format is intentionally human-readable so you can `tail -f` it
or pipe it to a structured-log shipper. Tolerant of corrupted trailing
lines (a partial flush during a crash is dropped silently during
recovery).

Suitable for: integration tests, single-node deployments. Not suitable
for: multi-coordinator production deployments — every coordinator
process would need to see the same log file, which file-system
semantics don't reliably guarantee under contention.

```csharp
services.AddSingleton<ITransactionLog>(_ =>
    new FileBasedTransactionLog("/var/dtde/tx-log.jsonl"));
```

### Custom implementations

For multi-coordinator production, implement `ITransactionLog` against a
shared durable store: PostgreSQL table, Redis with persistence, etc.
The interface is small (six methods) and idempotent — duplicates from
retried writes are fine.

## Wiring it up

`InMemoryTransactionLog` is registered automatically by
`AddDtdeDbContext`. To use a different one, register it **before**
`AddDtdeDbContext`:

```csharp
services.AddSingleton<ITransactionLog>(_ =>
    new FileBasedTransactionLog(
        Path.Combine(AppContext.BaseDirectory, "tx-log.jsonl")));

services.AddDtdeDbContext<AppDbContext>(
    (db, conn) => db.UseSqlite(conn ?? "Data Source=app.db"),
    dtde => dtde.AddShards("EU", "US", "APAC"));
```

The coordinator picks up whatever `ITransactionLog` is registered.

## Running recovery

Call `RecoverAsync` once at startup, *before* the application accepts
traffic:

```csharp
var coordinator = scope.ServiceProvider
    .GetRequiredService<ICrossShardTransactionCoordinator>();

var resolved = await coordinator.RecoverAsync();
logger.LogInformation("Resolved {Count} in-doubt transactions on startup.", resolved);
```

The method returns the number of transactions it resolved. Zero is the
boring (and most common) case — there were no in-doubt transactions.

You can also poke the log directly to inspect what's pending:

```csharp
var log = scope.ServiceProvider.GetRequiredService<ITransactionLog>();
var inDoubt = await log.GetInDoubtTransactionsAsync();

foreach (var entry in inDoubt)
{
    logger.LogWarning(
        "In-doubt tx {TxId}: enlisted={Enlisted}, prepared={Prepared}",
        entry.TransactionId,
        entry.EnlistedParticipants.Count,
        entry.PreparedParticipants.Count);
}
```

## Operational hygiene

- **Truncate the log periodically.** Recovery only reads the
  `Started` entries — terminal entries are noise. The
  `FileBasedTransactionLog` doesn't rotate, so for long-lived
  deployments, schedule a quiet-hours job that copies the log
  somewhere else and replaces it with an empty file. Beware: do this
  while no transactions are in flight.
- **Monitor the in-doubt count.** A persistently-non-zero in-doubt
  count between recovery scans signals trouble — most likely a shard
  is unreachable. Page on it.
- **Don't share a `FileBasedTransactionLog` across processes.** It's
  written via a single `SemaphoreSlim`; multi-process write
  coordination via OS file locks works on most filesystems but isn't
  what the implementation targets.

## What recovery does *not* do

- **Drive participants to commit.** When the global decision is
  "commit", DTDE records that decision in the log. The participants'
  open prepared transactions either auto-commit when the connection is
  re-established (provider-dependent), or are picked up by external
  monitoring. DTDE does not (yet) include a mechanism to forcibly
  commit a prepared local transaction from outside its original
  process — that's distributed-transaction-manager territory.
- **Detect non-DTDE in-doubt transactions.** The log only tracks
  transactions DTDE coordinated. If you're using DTDE alongside DTC,
  you'll need both recovery paths.
- **Replay log on every startup.** `RecoverAsync` is opt-in; nothing
  fires it for you. Build it into your startup pipeline if recovery
  matters to you.

## See also

- [Cross-shard transactions](cross-shard-transactions.md) — the
  transaction surface itself.
- The runnable
  [`Dtde.Samples.Transactions`](https://github.com/yohasacura/dtde/tree/main/samples/Dtde.Samples.Transactions)
  project — wired up with `FileBasedTransactionLog` and a `/recovery`
  endpoint.
