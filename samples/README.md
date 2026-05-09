# DTDE Sample Projects

Eight runnable Web API samples covering every shipping DTDE feature.

| Project | What it shows |
|---|---|
| [`Dtde.Sample.WebApi`](./Dtde.Sample.WebApi/) | Smallest possible getting-started app. |
| [`Dtde.Samples.RegionSharding`](./Dtde.Samples.RegionSharding/) | Property-value sharding by `Region` (`ShardBy`). |
| [`Dtde.Samples.DateSharding`](./Dtde.Samples.DateSharding/) | Time-bucketed sharding (`ShardByDate`). |
| [`Dtde.Samples.HashSharding`](./Dtde.Samples.HashSharding/) | Hash-based even distribution (`ShardByHash`). |
| [`Dtde.Samples.MultiTenant`](./Dtde.Samples.MultiTenant/) | Tenant-id sharding for SaaS. |
| [`Dtde.Samples.Combined`](./Dtde.Samples.Combined/) | Multiple strategies in one app — uses **shard groups** to give each entity its own topology. |
| **[`Dtde.Samples.Transactions`](./Dtde.Samples.Transactions/)** | **NEW.** Cross-shard 2PC, savepoints, read-after-write, crash-recovery log. |
| **[`Dtde.Samples.BulkOperations`](./Dtde.Samples.BulkOperations/)** | **NEW.** `BulkInsert/Update/Delete`, `ExecuteStreamingAsync`, custom `IBulkInsertProvider`. |

## What's in the box

### Sharding

Property-value sharding is the simplest case — one entity, one shard key:

```csharp
modelBuilder.Entity<Customer>().ShardBy(c => c.Region);

// Program.cs
dtde.AddShards("EU", "US", "APAC");
```

Hash-based sharding spreads load evenly across a fixed shard count:

```csharp
modelBuilder.Entity<UserProfile>().ShardByHash(u => u.UserId, shardCount: 8);

dtde.AddShards("0", "1", "2", "3", "4", "5", "6", "7");
```

Date-based sharding partitions by time interval:

```csharp
modelBuilder.Entity<Order>().ShardByDate(o => o.OrderDate, DateShardInterval.Year);

dtde.AddShards("2023", "2024", "2025");
```

### Shard groups

When two entities have **different shard topologies** in the same DbContext (e.g. eight hash buckets for users *and* three yearly buckets for orders), bind each to a named group:

```csharp
dtde => dtde
    .AddShardGroup("hash8", g => g.AddShards("0","1","2","3","4","5","6","7"))
    .AddShardGroup("years", g => g.AddShards("2023","2024","2025"));

modelBuilder.Entity<UserProfile>().ShardByHash(u => u.UserId, 8).UseShardGroup("hash8");
modelBuilder.Entity<Order>().ShardByDate(o => o.OrderDate).UseShardGroup("years");
```

Same local id in different groups (e.g. `"0"` in `hash8` vs `"0"` in `hash3`) refers to **different physical shards**. The `Combined` sample shows this in action.

### Cross-shard transactions (`Transactions` sample)

```csharp
await using var tx = await db.BeginCrossShardTransactionAsync(new CrossShardTransactionOptions
{
    IsolationLevel = CrossShardIsolationLevel.Serializable,
});

var fromParticipant = await ((CrossShardTransaction)tx).GetOrCreateParticipantAsync(euShard);
var toParticipant   = await ((CrossShardTransaction)tx).GetOrCreateParticipantAsync(usShard);

fromParticipant.Context.Set<Account>().Update(sender);
toParticipant.Context.Set<Account>().Update(receiver);

await tx.CommitAsync(); // 2PC
```

**Savepoints** roll back partial work without ending the whole transaction:

```csharp
await participant.CreateSavepointAsync("bonus");
// try optional work
await participant.RollbackToSavepointAsync("bonus");
// transaction stays open; only post-savepoint work was undone
```

**Read-after-write** — queries inside the transaction see uncommitted writes on the same shard automatically.

**Crash-recovery log** — register an `ITransactionLog` (default in-memory; ship-included `FileBasedTransactionLog` for persistence) and call `coordinator.RecoverAsync()` on startup to drive any in-doubt transactions to a terminal state.

### Bulk operations (`BulkOperations` sample)

```csharp
await db.BulkInsertAsync(events);                  // routes per shard
await db.BulkUpdateAsync<Event>(e => e.Type == "click",
    setters => setters.SetProperty(e => e.Payload, "<redacted>"));   // EF 10
await db.BulkDeleteAsync<Event>(e => e.CreatedAt < cutoff);

await foreach (var ev in executor.ExecuteStreamingAsync(db.Set<Event>().AsQueryable()))
{
    // streams concurrently across all shards into a bounded channel
}
```

Plug in `SqlBulkCopy` (SQL Server), PG `COPY`, etc. via:

```csharp
services.AddSingleton<IBulkInsertProvider, MyProviderSpecificBulkInsert>();
```

## Run any sample

```bash
cd samples/Dtde.Samples.HashSharding
dotnet run
# Swagger UI at http://localhost:5000/swagger
```

The HTTP file alongside each sample (`api-tests.http`, where present) has ready-made requests for every endpoint.
