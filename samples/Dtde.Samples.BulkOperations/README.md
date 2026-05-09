# Dtde.Samples.BulkOperations

Demonstrates the **bulk operations** and **streaming query** surface:

- **`BulkInsertAsync`** — routes each entity to its target shard, batches per shard, single round-trip per shard.
- **`BulkUpdateAsync`** — set-based `UPDATE WHERE` fan-out across the entity's shard group. Both EF 7-9 (`SetPropertyCalls<T>`) and EF 10 (`UpdateSettersBuilder<T>`) shapes covered via `#if`.
- **`BulkDeleteAsync`** — set-based `DELETE WHERE` fan-out.
- **`ExecuteStreamingAsync`** — `IAsyncEnumerable<T>` with bounded `Channel<T>` buffering. Constant memory regardless of result-set size.
- **`IBulkInsertProvider`** — pluggable per-provider bulk loader. The custom `LoggingBulkInsertProvider` demonstrates the plug-in shape; in production this is where you'd call `SqlBulkCopy`, PG `COPY`, Oracle direct-path, etc.

## Endpoints

| Method | Path | Demo |
|---|---|---|
| POST | `/seed?count=N` | Bulk-insert N synthetic events spread across EU/US/APAC. |
| POST | `/anonymise-clicks` | Bulk-update every `Type=click` event's payload to `<redacted>`. |
| POST | `/purge-old?before=YYYY-MM-DD` | Bulk-delete events older than a cutoff. |
| GET | `/stream?bufferSize=N` | Streaming fan-out as `IAsyncEnumerable<Event>`. |
| GET | `/stream-summary` | Streaming + projection to a small DTO. |

## Run it

```bash
cd samples/Dtde.Samples.BulkOperations
dotnet run
```

```http
POST http://localhost:5000/seed?count=10000
POST http://localhost:5000/anonymise-clicks
POST http://localhost:5000/purge-old?before=2024-01-01
GET  http://localhost:5000/stream?bufferSize=256
```

The custom provider's logs will appear in the console for every per-shard insert (it's the chain's first non-default `IBulkInsertProvider`).
