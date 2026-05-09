# Dtde.Samples.RegionSharding — property-based sharding

Demonstrates **property-based sharding** for data residency / multi-region
deployments. `Customer` rows for the EU region land in the EU shard, US rows
in the US shard, etc. Co-located `Order` and `OrderItem` rows follow the
same partition.

## Run

```bash
cd samples/Dtde.Samples.RegionSharding
dotnet run
```

Open `http://localhost:5000/swagger` for the API explorer.

## Key files

| File | What it shows |
|---|---|
| [`Program.cs`](Program.cs) | One shard per region (EU/US/APAC). |
| [`Data/RegionShardingDbContext.cs`](Data/RegionShardingDbContext.cs) | `ShardBy(c =&gt; c.Region)` on three related entities. |
| [`Controllers/CustomersController.cs`](Controllers/CustomersController.cs) | Region-scoped queries; partition pruning is automatic. |
| [`api-tests.http`](api-tests.http) | Drop-in REST Client examples. |

## Try it

`POST /api/customers` with `{ "region": "EU", ... }` writes to the EU shard.
`GET /api/customers?region=EU` queries only the EU shard. Cross-region scans
fan out across all shards transparently — watch the SQL logs to see DTDE
splitting the request.
