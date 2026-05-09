# Dtde.Samples.MultiTenant — SaaS multi-tenancy

Demonstrates **tenant-isolated sharding** for SaaS applications. Every
domain row carries a `TenantId`, and `ShardBy(TenantId)` ensures a tenant's
data only ever touches its own shard — strong logical isolation without
running a separate database per tenant.

Entities:

- `Tenant` — master directory (not sharded; lives in the catalog shard).
- `Project`, `ProjectTask`, `TaskComment` — sharded by `TenantId`, all
  co-located so per-tenant joins stay single-shard.

The sample also showcases a **tenant context middleware** that resolves
`TenantId` from any of: `X-Tenant-Id` header, `/api/tenant/{tenantId}/...`
route value, or `?tenantId=...` query string — whichever arrives first wins.

## Run

```bash
cd samples/Dtde.Samples.MultiTenant
dotnet run
```

Open `http://localhost:5000/swagger`. Pass tenant id via header for most
endpoints:

```http
GET /api/projects
X-Tenant-Id: acme-corp
```

## Key files

| File | What it shows |
|---|---|
| [`Program.cs`](Program.cs) | Per-tenant shard registration + seed data. |
| [`Data/MultiTenantDbContext.cs`](Data/MultiTenantDbContext.cs) | Mixed model: `Tenant` (catalog) + `Project*` (sharded). |
| [`Middleware/TenantMiddleware.cs`](Middleware/TenantMiddleware.cs) | Tenant resolution (header / route / query). |
| [`Controllers/TenantsController.cs`](Controllers/TenantsController.cs) | Catalog operations — list/create tenants. |
| [`Controllers/ProjectsController.cs`](Controllers/ProjectsController.cs) | Per-tenant CRUD; tenant id is implicit from middleware. |

## Try it

Create two tenants, then issue requests with different `X-Tenant-Id` headers.
A tenant only sees its own projects, tasks, and comments — even if the IDs
collide across tenants.
