# Guides

Topic-by-topic walk-throughs of every shipping DTDE feature.

## Start here

| Guide | What it covers | Time |
|---|---|---|
| **[Getting started](getting-started.md)** | Sharded `DbContext`, three logical shards, table/database/mixed mode, working LINQ query — end to end. | 5 min |

## Sharding

| Guide | What it covers |
|---|---|
| **[Sharding guide](sharding-guide.md)** | All four strategies (`ShardBy`, `ShardByHash`, `ShardByDate`, `UseManualSharding`), the three storage modes, **shard groups** for per-entity topologies, and routing rules. |
| **[Temporal guide](temporal-guide.md)** | Bi-temporal entities, `ValidAt<T>(date)`, `AllVersions<T>()`, point-in-time queries. |

## Transactions and bulk operations

| Guide | What it covers |
|---|---|
| **[Cross-shard transactions](cross-shard-transactions.md)** | `BeginCrossShardTransactionAsync`, the 2PC protocol, savepoints, read-after-write inside a transaction, isolation levels, retry policy. |
| **[Bulk operations](bulk-operations.md)** | `BulkInsertAsync`, `BulkUpdateAsync`, `BulkDeleteAsync`, `ExecuteStreamingAsync`, pluggable `IBulkInsertProvider` (SqlBulkCopy / PG COPY / etc.). |
| **[Transaction log and recovery](transaction-log-and-recovery.md)** | `ITransactionLog`, `FileBasedTransactionLog`, `coordinator.RecoverAsync()`, the 2PC recovery rule. |

## Migration and reference

| Guide | What it covers |
|---|---|
| **[Migration guide](migration-guide.md)** | Migrating an existing EF Core project to DTDE. |
| **[API reference](../wiki/api-reference.md)** | Public type catalogue. |
| **[Architecture](../wiki/architecture.md)** | Internal layering, the three projects, key abstractions. |
| **[Configuration](../wiki/configuration.md)** | Every option on `DtdeOptionsBuilder`, JSON shard config schema. |
| **[Troubleshooting](../wiki/troubleshooting.md)** | Common errors and fixes. |

## Samples

Eight runnable Web API samples — one per concept. See
[`samples/`](https://github.com/yohasacura/dtde/tree/main/samples).
