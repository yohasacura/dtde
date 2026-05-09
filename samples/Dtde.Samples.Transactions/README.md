# Dtde.Samples.Transactions

Demonstrates the full **cross-shard transaction surface**:

- **`BeginCrossShardTransactionAsync`** — explicit two-phase commit across shards.
- **Savepoints** — within-shard partial rollback (`CreateSavepointAsync` / `RollbackToSavepointAsync`).
- **Read-after-write** inside a transaction — queries see uncommitted writes on the same shard.
- **Crash-recovery transaction log** — `FileBasedTransactionLog` persists lifecycle events; `coordinator.RecoverAsync()` replays them on startup.

## Endpoints

| Method | Path | Demo |
|---|---|---|
| POST | `/transfer` | Atomic cross-shard transfer between two regions. |
| POST | `/credit-with-bonus` | Savepoint demo — try a bonus credit, roll it back if a business rule rejects. |
| POST | `/within-tx-rollup` | Insert + query inside the same transaction; the query sees the uncommitted insert. |
| GET | `/recovery` | Replay the durable log; resolves any in-doubt transactions. |

## Run it

```bash
cd samples/Dtde.Samples.Transactions
dotnet run
```

Then `POST` examples:

```http
POST http://localhost:5000/transfer
Content-Type: application/json

{
  "fromRegion":   "EU",
  "fromAccountId": 1,
  "toRegion":     "US",
  "toAccountId":   2,
  "amount":      100.0
}
```

```http
POST http://localhost:5000/credit-with-bonus
Content-Type: application/json

{
  "region":       "EU",
  "accountId":    1,
  "baseAmount":  50.0,
  "bonusAmount": 10.0,
  "rejectBonus":  true
}
```

The transaction log is at `bin/.../tx-log.jsonl` — open it to inspect the recorded lifecycle events.
