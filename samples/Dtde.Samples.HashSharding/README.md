# Dtde.Samples.HashSharding — hash-based sharding

Demonstrates **hash-based sharding** for even data distribution. The shard key
(here, `UserId`) is hashed into one of N buckets — distribution is uniform,
hotspots are prevented, and lookups by key resolve to a single shard with no
fan-out.

Three co-located entities all hash on the same key (`UserId`, 8 shards):

- `UserProfile`
- `UserSession`
- `UserActivity`

Co-location means a `UserProfile.UserId == X` lives in the same shard as all
that user's sessions and activity, so per-user reads stay single-shard.

## Run

```bash
cd samples/Dtde.Samples.HashSharding
dotnet run
```

Open `http://localhost:5000/swagger`.

## Key files

| File | What it shows |
|---|---|
| [`Program.cs`](Program.cs) | Shard registration for 8 hash partitions. |
| [`Data/HashShardingDbContext.cs`](Data/HashShardingDbContext.cs) | `ShardByHash(u =&gt; u.UserId, shardCount: 8)`. |
| [`Controllers/UsersController.cs`](Controllers/UsersController.cs) | Single-shard lookups by user id. |
| [`Controllers/SessionsController.cs`](Controllers/SessionsController.cs) | Co-located queries (sessions for one user). |

## Try it

`GET /api/users/{id}` resolves to one shard via the hash. Counting all active
sessions across the population (`GET /api/sessions/stats`) fans out to all 8
shards in parallel — that's where the hash strategy's read-throughput
advantage shows up.
