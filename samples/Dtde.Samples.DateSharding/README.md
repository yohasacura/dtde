# Dtde.Samples.DateSharding — time-bucketed sharding

Demonstrates **date-range sharding** for time-series workloads. Three entities,
three different bucketing intervals:

- `Transaction` — monthly partitions (`DateShardInterval.Month`).
- `AuditLog` — daily partitions (`DateShardInterval.Day`).
- `MetricDataPoint` — quarterly partitions (`DateShardInterval.Quarter`).

This is the canonical pattern for financial transaction history, audit logs
with retention policies, and metrics. It enables hot/warm/cold tiering
naturally — recent partitions stay on fast storage; old ones move to archive.

## Run

```bash
cd samples/Dtde.Samples.DateSharding
dotnet run
```

Open `http://localhost:5000/swagger`.

## Key files

| File | What it shows |
|---|---|
| [`Program.cs`](Program.cs) | Shard registration with date ranges and tiers (Hot/Warm/Cold). |
| [`Data/DateShardingDbContext.cs`](Data/DateShardingDbContext.cs) | `ShardByDate(...)` on three different intervals. |
| [`Controllers/TransactionsController.cs`](Controllers/TransactionsController.cs) | Date-range queries; partition pruning skips irrelevant shards. |

## Try it

`GET /api/transactions?from=2025-01-01&to=2025-01-31` only scans the Jan 2025
shard. `GET /api/transactions?from=2024-01-01&to=2025-12-31` fans out across
all relevant monthly partitions. Compare query times to a single-table
baseline — see [`/benchmarks/Dtde.Benchmarks`](../../benchmarks/Dtde.Benchmarks)
for the comparable workload.
