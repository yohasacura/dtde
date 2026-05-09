# DTDE — Distributed Temporal Data Engine

Transparent horizontal **sharding**, **bi-temporal versioning**, and
**cross-shard transactions** for Entity Framework Core. You write
standard LINQ; DTDE handles routing, partition pruning, point-in-time
reads, and two-phase commits across shards.

```csharp
// Standard EF Core LINQ — DTDE prunes to the EU shard automatically.
var euCustomers = await db.Customers
    .Where(c => c.Region == "EU")
    .ToListAsync();

// Point-in-time query — fans out to the shards that hold valid rows.
var asOfLastMonth = await db
    .ValidAt<Contract>(DateTime.UtcNow.AddMonths(-1))
    .ToListAsync();

// Cross-shard atomic write — 2PC across EU and US.
await using var tx = await db.BeginCrossShardTransactionAsync();
// ... writes to multiple shards ...
await tx.CommitAsync();
```

## Install

```bash
dotnet add package Dtde.EntityFramework
```

`Dtde.EntityFramework` transitively pulls in `Dtde.Core` and
`Dtde.Abstractions`. Reference the lower-level packages directly only
for advanced provider scenarios.

## What's in the box

| Capability | Description |
|---|---|
| **Sharding strategies** | `ShardBy` (property value), `ShardByHash` (even distribution), `ShardByDate` (time bucketing), `UseManualSharding` (pre-existing tables). |
| **Storage modes** | Table-mode (one DB, many tables), database-mode (one DB per shard), mixed-mode (per-shard tables across multiple DBs). |
| **Shard groups** | Per-entity shard topologies — eight hash buckets for users *and* three yearly buckets for orders, in the same DbContext. |
| **Cross-shard transactions** | `BeginCrossShardTransactionAsync`, 2PC, savepoints, read-after-write, retry policy, isolation levels, **crash-recovery transaction log**. |
| **Bulk operations** | `BulkInsertAsync`, `BulkUpdateAsync`, `BulkDeleteAsync` with provider-pluggable bulk loaders (SqlBulkCopy / PG COPY / etc.). |
| **Streaming queries** | `ExecuteStreamingAsync` returns `IAsyncEnumerable<T>` with bounded buffering — constant memory regardless of result-set size. |
| **Bi-temporal entities** | `HasTemporalValidity`, `ValidAt<T>`, `ValidBetween<T>`, `AllVersions<T>`, `CreateNewVersion`. |
| **Multi-targeting** | .NET 8, .NET 9, .NET 10. |

## Quick start

→ **[Getting started](guides/getting-started.md)** — sharded
`DbContext`, three logical shards, working LINQ in 5 minutes.

## Topic guides

- **[Sharding](guides/sharding-guide.md)** — strategies, storage modes, shard groups, routing rules.
- **[Cross-shard transactions](guides/cross-shard-transactions.md)** — 2PC, savepoints, read-after-write.
- **[Bulk operations](guides/bulk-operations.md)** — set-based fan-out, streaming, custom bulk providers.
- **[Transaction log and recovery](guides/transaction-log-and-recovery.md)** — durable lifecycle log, `RecoverAsync`.
- **[Temporal versioning](guides/temporal-guide.md)** — bi-temporal entities, point-in-time queries.
- **[Migration guide](guides/migration-guide.md)** — moving an existing EF Core project to DTDE.

## Reference

- **[API reference](wiki/api-reference.md)** — public type catalogue.
- **[Architecture](wiki/architecture.md)** — internal layering, key abstractions.
- **[Configuration](wiki/configuration.md)** — every option on `DtdeOptionsBuilder`.
- **[Troubleshooting](wiki/troubleshooting.md)** — common errors and fixes.

## Source and samples

- **[GitHub](https://github.com/yohasacura/dtde)** — source, issues, discussions.
- **[Samples](https://github.com/yohasacura/dtde/tree/main/samples)** — eight runnable Web API projects, one per major concept.

## License

MIT. See [LICENSE](https://github.com/yohasacura/dtde/blob/main/LICENSE).
