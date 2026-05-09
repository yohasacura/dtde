# Bulk operations

DTDE ships three set-based extension methods on `DtdeDbContext` plus a
streaming query API. They are the right tool when:

- You're inserting more than a few hundred entities.
- You're updating or deleting rows by predicate without needing to
  materialise them first.
- You're streaming a large result set and don't want to buffer
  everything in memory.

All four methods route per-shard automatically — they respect the
entity's shard group and the entity-to-shard mapping declared in
`OnModelCreating`. They also participate transparently in an ambient
cross-shard transaction when one is active (see
[cross-shard transactions](cross-shard-transactions.md)).

## `BulkInsertAsync`

Routes each entity to its target shard, batches per shard, and writes
each shard with a single round-trip. With more than one shard touched,
the work is wrapped in a cross-shard transaction (2PC); single-shard
input takes the fast path.

```csharp
var newCustomers = new List<Customer>
{
    new() { Region = "EU", ... },
    new() { Region = "US", ... },
    // ... thousands more
};

var inserted = await db.BulkInsertAsync(newCustomers);
```

### Provider-specific bulk loaders

The default path uses `AddRangeAsync` + `SaveChangesAsync`. For really
large batches (≫ 10 000 rows) you'll want a provider-specific bulk
loader: `SqlBulkCopy` for SQL Server, `COPY FROM STDIN` for PostgreSQL,
direct-path inserts for Oracle, etc.

DTDE makes that pluggable via `IBulkInsertProvider`:

```csharp
public sealed class SqlServerBulkInsertProvider : IBulkInsertProvider
{
    public bool CanHandle(DbContext context)
        => context.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer";

    public async Task<int> BulkInsertAsync<TEntity>(
        DbContext context,
        IReadOnlyCollection<TEntity> entities,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        var connection = context.Database.GetDbConnection();
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();

        // ... drive SqlBulkCopy against `connection` and `transaction` ...

        return entities.Count;
    }
}
```

Register it **before** `AddDtdeDbContext`:

```csharp
services.AddSingleton<IBulkInsertProvider, SqlServerBulkInsertProvider>();
services.AddDtdeDbContext<AppDbContext>(...);
```

DTDE resolves providers in registration order with the
`DefaultBulkInsertProvider` always at the tail — the first provider
whose `CanHandle(context)` returns `true` wins. Multiple custom
providers can co-exist (e.g. one for SQL Server, one for Postgres).

## `BulkUpdateAsync`

Set-based `UPDATE WHERE` fanned out across every shard in the entity's
shard group. No `SELECT` round-trip, no change-tracker overhead, no
loading rows into memory.

The exact signature depends on EF Core version:

=== "EF Core 7 / 8 / 9"

    ```csharp
    var updated = await db.BulkUpdateAsync<Event>(
        e => e.Type == "old",
        p => p.SetProperty(e => e.Type, "new")
              .SetProperty(e => e.UpdatedAt, DateTime.UtcNow));
    ```

=== "EF Core 10"

    ```csharp
    var updated = await db.BulkUpdateAsync<Event>(
        e => e.Type == "old",
        setters => setters
            .SetProperty(e => e.Type, "new")
            .SetProperty(e => e.UpdatedAt, DateTime.UtcNow));
    ```

The two signatures track EF Core's own API changes (`SetPropertyCalls<T>`
in EF 7-9, `UpdateSettersBuilder<T>` in EF 10). DTDE picks the right
overload at compile time via `#if NET10_0_OR_GREATER`.

## `BulkDeleteAsync`

```csharp
var deleted = await db.BulkDeleteAsync<Event>(e => e.CreatedAt < cutoff);
```

Same fan-out semantics as `BulkUpdateAsync`. Each shard gets a single
`DELETE WHERE`, no `SELECT` first.

## Streaming queries: `ExecuteStreamingAsync`

For very large fan-outs, materialising every row in memory at once
isn't acceptable. `ExecuteStreamingAsync` returns
`IAsyncEnumerable<TEntity>`:

```csharp
var executor = sp.GetRequiredService<IShardedQueryExecutor>();

await foreach (var ev in executor.ExecuteStreamingAsync(
    db.Set<Event>().AsQueryable()))
{
    // process one at a time — constant memory regardless of result-set size
}
```

Internally each shard's results are pulled by a concurrent producer
into a bounded `Channel<TEntity>`; the consumer pulls in arrival order.
The default buffer is `shardCount × 64`, with a minimum of 16 — tweak
via the `bufferSize` parameter:

```csharp
await foreach (var ev in executor.ExecuteStreamingAsync(query, bufferSize: 256))
{
    ...
}
```

Order is **not** guaranteed across shards — apply `OrderBy` on the
result if you need it. Cancellation is propagated to the per-shard
producers and through the `IAsyncEnumerable` the moment you stop
enumerating, so abandoning the stream tears down the producers
cleanly.

## Bulk operations and transactions

Inside an ambient `BeginCrossShardTransactionAsync` scope, every bulk
operation is routed through that transaction's participants:

```csharp
await using var tx = await db.BeginCrossShardTransactionAsync();

await db.BulkInsertAsync(seed);              // routes through tx participants
await db.BulkUpdateAsync<Event>(e => ...,
    setters => setters.SetProperty(...));    // also through tx
await db.BulkDeleteAsync<Event>(e => ...);   // also through tx

await tx.CommitAsync();
// Or RollbackAsync — every per-shard bulk operation is undone atomically.
```

Outside a transaction, each bulk operation manages its own short-lived
transaction (single-shard fast path or 2PC across multiple shards). The
multi-shard 2PC ensures the bulk operation is atomic across the group.

## Performance notes

- **Don't fan out on a single-shard predicate.** A `Where(e => e.Region == "EU")`
  prunes to one shard for `BulkUpdateAsync`/`BulkDeleteAsync` —
  pre-filtering is free.
- **Custom providers should respect the open transaction.** Read
  `context.Database.CurrentTransaction` and pass its `DbTransaction` to
  `SqlBulkCopy` / `NpgsqlBinaryImporter` — otherwise your bulk write
  isn't part of the cross-shard 2PC and won't roll back together.
- **Streaming is per-row, not per-batch.** If you're processing
  10 million rows and want batched I/O on the consumer side, buffer
  manually inside the `await foreach`.

## See also

- The runnable [`Dtde.Samples.BulkOperations`](https://github.com/yohasacura/dtde/tree/main/samples/Dtde.Samples.BulkOperations) project.
- The [cross-shard transactions guide](cross-shard-transactions.md).
- The [sharding guide](sharding-guide.md) — how the entity-to-shard
  mapping drives bulk routing.
