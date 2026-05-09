# Troubleshooting

Common errors and how to fix them. For deep dives, see
[architecture](architecture.md) and the
[sharding guide](../guides/sharding-guide.md).

## Configuration errors

### `InvalidOperationException: Entity '...' is bound to shard group '...', but no such group is registered.`

The entity declared `UseShardGroup("name")`, but the application never
called `AddShardGroup("name", ...)` (or `AddShards(...)` for the
default group).

**Fix.** Match the names exactly:

```csharp
// Program.cs
dtde => dtde
    .AddShardGroup("hash8", g => g.AddShards("0","1",...,"7"));

// OnModelCreating
modelBuilder.Entity<UserProfile>().ShardByHash(u => u.UserId, 8)
    .UseShardGroup("hash8");
```

The error fires at first DbContext model build, not at query time.

### `InvalidOperationException: A cross-shard transaction is already active in the current context.`

You called `BeginCrossShardTransactionAsync` while another transaction
was still active in the same `AsyncLocal` scope.

**Fix.** Make sure the previous transaction is fully disposed (use
`await using` and let the scope close) before beginning a new one.
Nested cross-shard transactions are not supported.

### `ShardNotFoundException: Shard 'xyz' not found in registry.`

Your write router computed a target shard id that wasn't registered.
For property-value sharding, the row's shard-key value doesn't match
any `AddShard(...)` id. For hash sharding, the entity's hash modulo
`shardCount` produces an id outside the registered range.

**Fix.** Either register the missing shard, or filter out the rows
that map to it before writing.

### Sample CA1873 build error

If you bring DTDE source into a project with `TreatWarningsAsErrors`
enabled, you may see CA1873 from the samples. The fix is in
`samples/Directory.Build.props` — add `CA1873` to `NoWarn`. Sample
projects intentionally favour readability over strict library-grade
analysis.

## Runtime errors

### `Cannot create a DbSet for 'Foo' because this type is not included in the model of the context`

You're querying an entity through a per-shard context that excluded it
because the entity is in a different shard group.

**Fix.** Either bind the entity to the right group, or query through
the parent context (which has every entity in its model).

### Database `is locked` (SQLite shared-cache)

You're running an integration test that opens an "anchor" SqliteConnection
to verify rows while a participant transaction is still open. SQLite's
shared-cache mode reports `SQLITE_LOCKED` even between connections in
the same process.

**Fix.** End the `await using` scope around the transaction (which
disposes the participant) before reading via the anchor connection.

### Bulk insert puts every row in one shard

Your entity declared a `ShardBy*` annotation, but the
`MetadataRegistry` doesn't have its sharding configuration — likely
because `BulkInsertAsync` was called before the lazy backfill ran.

**Fix.** This is fixed since the metadata-registry-backfill feature
landed. Ensure you're on the latest DTDE; if you still see it, file an
issue with a repro.

### `Entity '...' has no shard key configured for hash sharding.`

You called `ShardByHash(u => u.UserId, 8)` but the property doesn't
return a stable hash (e.g. it's `null`). Hash-modulo-N requires a
non-null key.

**Fix.** Make the shard-key property non-nullable, or pre-populate it
before the first save.

## Cross-shard transaction errors

### `TransactionPrepareException: Prepare phase failed.`

One participant voted to abort during prepare. The exception's
`InnerException` carries the original cause (constraint violation,
deadlock, timeout, etc.).

**Fix.** Read the inner exception and address its cause. Transient
errors (deadlock, timeout, dropped connection) auto-retry up to
`CrossShardTransactionOptions.MaxRetryAttempts` if you used
`ExecuteInTransactionAsync`; for an explicit `BeginCrossShardTransactionAsync`
you handle retries yourself.

### `TransactionCommitException: Commit phase failed. ... Transaction is in-doubt.`

Some participants committed; others didn't. This is the worst-case
2PC outcome. If you have a durable `ITransactionLog` registered (e.g.
`FileBasedTransactionLog`), call `coordinator.RecoverAsync()` on
restart — recovery will resolve the global decision based on whether
every enlisted participant logged a `prepared` vote.

If you don't have a durable log, the transaction is unrecoverable and
you'll need manual reconciliation. Production deployments should
register a durable log; see [recovery](../guides/transaction-log-and-recovery.md).

### `TransactionTimeoutException: Transaction timed out.`

The cross-shard transaction exceeded `CrossShardTransactionOptions.Timeout`
(default 60 s).

**Fix.** Either reduce the work inside the scope (split into multiple
shorter transactions) or increase the timeout. For long-running data
migrations, use `CrossShardTransactionOptions.LongRunning` (5 min).

## Diagnostic tools

Enable verbose logging:

```csharp
services.AddDtdeDbContext<AppDbContext>(
    (db, conn) => db.UseSqlServer(conn ?? "..."),
    dtde => dtde
        .AddShards("EU", "US")
        .EnableDiagnostics());

services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
```

DTDE emits structured logs in the event-id ranges:

| Range | Source |
|---|---|
| 1000–9999 | `Dtde.EntityFramework.Diagnostics.LogMessages` (queries, writes, batch). |
| 10000–10199 | `Dtde.Core.Transactions.TransactionLogMessages` (cross-shard transaction lifecycle). |

## FAQ

### Does DTDE work with [provider X]?

Anything with an EF Core provider works. We test against SQLite and
the in-memory provider in CI; SQL Server, PostgreSQL, MySQL, and
Oracle all work in practice. Only the bulk-insert provider's
provider-specific paths (SqlBulkCopy, COPY, etc.) are
provider-dependent — see the [bulk operations guide](../guides/bulk-operations.md).

### Can I add shards at runtime?

Yes — call `((Dtde.Core.Metadata.ShardRegistry)options.ShardRegistry).AddShard(metadata)`
on a running registry. The corresponding `IShardGroupRegistry`
re-partitions on next read. The model cache evicts on next
`DbContext` construction; existing contexts continue to use their
cached models until disposed.

### Can I run DTDE in AOT / trim mode?

Not yet. DTDE relies on `Expression.Property` / `Type.GetProperty` for
dynamic entity-shape introspection. Trim/AOT readiness is a roadmap
item — see the development plan.

### Does DTDE support nested transactions?

No. `CurrentTransaction` is `AsyncLocal`-scoped; calling
`BeginCrossShardTransactionAsync` inside an active scope throws.
Inside a transaction you can use **savepoints** for partial rollback —
see [cross-shard transactions](../guides/cross-shard-transactions.md#savepoints-within-shard-partial-rollback).

### Does DTDE support distributed deadlock detection?

No. DTDE relies on each provider's local deadlock detection plus the
coordinator's retry policy.

### How do I debug `Cannot create a DbSet` in tests?

Verify that the per-shard context customizer didn't `Ignore` the
entity. The customizer only excludes entities whose declared shard
group differs from the active shard's group. Check
`entity.UseShardGroup("...")` matches `dtde.AddShardGroup("...", ...)`.

## Getting help

- [GitHub issues](https://github.com/yohasacura/dtde/issues) — bug reports.
- [GitHub discussions](https://github.com/yohasacura/dtde/discussions) — questions.
- The [development plan](../development-plan/) — what's on the roadmap.
