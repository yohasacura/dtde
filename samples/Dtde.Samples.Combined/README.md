# Dtde.Samples.Combined — mixed strategies + cross-shard transactions

The most ambitious sample. Demonstrates **multiple sharding strategies in one
application** plus **cross-shard transactions** for ACID writes that span
shards.

Domain: a financial-services-style application where every entity has its
own residency and lifecycle requirements.

| Entity | Strategy | Why |
|---|---|---|
| `Account` | `ShardBy(Region)` | Customer data residency / GDPR. |
| `AccountTransaction` | `ShardByDate(TransactionDate, Month)` | Time-series; archive old months cheaply. |
| `RegulatoryDocument` | `ShardBy(DocumentType)` | Logical grouping by document class. |
| `ComplianceAudit` | `ShardByHash(EntityReference, 8)` | Even distribution; no hotspots. |

## Run

```bash
cd samples/Dtde.Samples.Combined
dotnet run
```

Open `http://localhost:5000/swagger`.

## Key files

| File | What it shows |
|---|---|
| [`Program.cs`](Program.cs) | Mixed shard registration. |
| [`Data/CombinedDbContext.cs`](Data/CombinedDbContext.cs) | All four sharding strategies in one `OnModelCreating`. |
| [`Controllers/AccountsController.cs`](Controllers/AccountsController.cs) | Region-scoped CRUD. |
| [`Controllers/TransactionsController.cs`](Controllers/TransactionsController.cs) | Date-bucketed reads/writes. |
| [`Controllers/CrossShardTransactionsController.cs`](Controllers/CrossShardTransactionsController.cs) | Two-phase commit (2PC) across multiple shards. |

## Cross-shard transactions

The most interesting part. `CrossShardTransactionsController` shows how to
move funds between accounts in **different regions** atomically:

1. The coordinator (`ICrossShardTransactionCoordinator`) opens a transaction.
2. Each shard's participant prepares (Phase 1).
3. If everyone votes commit, the coordinator commits all participants
   (Phase 2). Otherwise, all roll back.

State machine: `Active → Preparing → Prepared → Committing → Committed`,
with rollback paths from any state. Read
[`docs/guides/cross-shard-transactions.md`](../../docs/guides/cross-shard-transactions.md)
for the protocol details.
