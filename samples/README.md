# DTDE Sample Projects

This directory contains comprehensive sample projects demonstrating different sharding strategies available in the Distributed Temporal Data Engine (DTDE).

## Sample Projects Overview

| Project | Sharding Strategy | Use Case |
|---------|------------------|----------|
| [Dtde.Sample.WebApi](./Dtde.Sample.WebApi/) | Basic | Simple getting started example |
| [Dtde.Samples.RegionSharding](./Dtde.Samples.RegionSharding/) | Property-Based | Multi-region data residency |
| [Dtde.Samples.DateSharding](./Dtde.Samples.DateSharding/) | Date-Based | Time-series data, audit logs |
| [Dtde.Samples.HashSharding](./Dtde.Samples.HashSharding/) | Hash-Based | Even data distribution |
| [Dtde.Samples.MultiTenant](./Dtde.Samples.MultiTenant/) | Property-Based (TenantId) | SaaS multi-tenancy |
| [Dtde.Samples.Combined](./Dtde.Samples.Combined/) | Mixed Strategies | Complex enterprise scenarios |

---

## 1. Region Sharding (`Dtde.Samples.RegionSharding`)

Demonstrates **property-based sharding by region** for data residency compliance.

### Entities
- **Customer**: Sharded by `Region` (EU, US, APAC)
- **Order**: Sharded by `Region` (co-located with customers)
- **OrderItem**: Sharded by `Region` (co-located with orders)

### Key Configuration
```csharp
// In DbContext.OnModelCreating()
modelBuilder.Entity<Customer>(entity =>
{
    entity.ShardBy(c => c.Region);
});
```

### Use Cases
- GDPR compliance (EU data stays in EU)
- Regional data centers
- Latency optimization by geography

---

## 2. Date Sharding (`Dtde.Samples.DateSharding`)

Demonstrates **date-based sharding** for time-series data.

### Entities
- **Transaction**: Sharded by `TransactionDate` (monthly)
- **AuditLog**: Sharded by `Timestamp` (daily)
- **MetricDataPoint**: Sharded by `Timestamp` (quarterly)

### Key Configuration
```csharp
// In DbContext.OnModelCreating()
modelBuilder.Entity<Transaction>(entity =>
{
    entity.ShardByDate(t => t.TransactionDate);
});
```

### Use Cases
- Financial transaction history
- Audit logs with retention policies
- Time-series metrics and analytics
- Hot/warm/cold data tiering

---

## 3. Hash Sharding (`Dtde.Samples.HashSharding`)

Demonstrates **hash-based sharding** for even data distribution.

### Entities
- **UserProfile**: Sharded by `UserId` hash (8 shards)
- **UserSession**: Sharded by `UserId` hash (8 shards)
- **UserActivity**: Sharded by `UserId` hash (8 shards)

### Key Configuration
```csharp
// In DbContext.OnModelCreating()
modelBuilder.Entity<UserProfile>(entity =>
{
    entity.ShardByHash(u => u.UserId, shardCount: 8);
});
```

### Use Cases
- High-volume user data
- Preventing hotspots from sequential IDs
- Horizontal scaling with predictable distribution
- Session management

---

## 4. Multi-Tenant (`Dtde.Samples.MultiTenant`)

Demonstrates **tenant-based sharding** for SaaS applications.

### Entities
- **Tenant**: Master lookup (not sharded)
- **Project**: Sharded by `TenantId`
- **ProjectTask**: Sharded by `TenantId` (co-located with projects)
- **TaskComment**: Sharded by `TenantId` (co-located with tasks)

### Key Configuration
```csharp
// In DbContext.OnModelCreating()
modelBuilder.Entity<Project>(entity =>
{
    entity.ShardBy(p => p.TenantId);
});
```

### Features
- Tenant context middleware (extracts from header/route/query)
- Complete data isolation per tenant
- Co-located related entities for efficient joins

### Tenant Resolution
- Header: `X-Tenant-Id: acme-corp`
- Route: `/api/tenant/{tenantId}/projects`
- Query: `?tenantId=acme-corp`

---

## 5. Combined Strategies (`Dtde.Samples.Combined`)

Demonstrates **multiple sharding strategies in one application** for complex enterprise scenarios.

### Entities and Strategies
| Entity | Strategy | Purpose |
|--------|----------|---------|
| Account | ShardBy(Region) | Data residency compliance |
| AccountTransaction | ShardByDate(TransactionDate, Month) | Time-series partitioning |
| RegulatoryDocument | ShardBy(DocumentType) | Logical grouping |
| ComplianceAudit | ShardByHash(EntityReference, 8) | Even distribution |

### Key Configuration
```csharp
// In DbContext.OnModelCreating()
modelBuilder.Entity<Account>(entity =>
{
    entity.ShardBy(a => a.Region);
});

modelBuilder.Entity<AccountTransaction>(entity =>
{
    entity.ShardByDate(t => t.TransactionDate, DateShardInterval.Month);
});

modelBuilder.Entity<ComplianceAudit>(entity =>
{
    entity.ShardByHash(a => a.EntityReference, shardCount: 8);
});
```

### Use Cases
- Financial services with multi-region compliance
- Banking systems with transaction history
- Regulatory document management
- Comprehensive audit trails

---

## Running the Samples

### Prerequisites
- .NET 9.0 SDK
- SQLite (bundled with samples)

### Build All Samples
```bash
cd samples
dotnet build
```

### Run Individual Sample
```bash
cd Dtde.Samples.HashSharding
dotnet run
```

### Access Swagger UI
Each sample exposes Swagger UI at `https://localhost:5001/swagger` when running.

---

## Configuration Patterns

### 1. Service Registration
```csharp
builder.Services.AddDtdeDbContext<YourDbContext>(
    dbOptions => dbOptions.UseSqlite("Data Source=app.db"),
    dtdeOptions =>
    {
        // Optional: Add pre-defined shards
        dtdeOptions.AddShard(shardBuilder => 
        {
            shardBuilder.WithId("shard-1")
                        .WithConnectionString("...");
        });
    });
```

### 2. Entity Configuration (Fluent API)
```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    
    modelBuilder.Entity<YourEntity>(entity =>
    {
        // Standard EF configuration
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Name).IsRequired();
        
        // DTDE sharding configuration
        entity.ShardBy(e => e.ShardKey);
        // OR
        entity.ShardByDate(e => e.CreatedAt, DateShardInterval.Month);
        // OR
        entity.ShardByHash(e => e.UserId, shardCount: 8);
    });
}
```

### 3. DbContext Inheritance
```csharp
public class YourDbContext : DtdeDbContext
{
    public YourDbContext(DbContextOptions<YourDbContext> options)
        : base(options)
    {
    }
    
    public DbSet<YourEntity> Entities => Set<YourEntity>();
}
```

---

## Sharding Method Reference

| Method | Description | Example |
|--------|-------------|---------|
| `ShardBy<T, K>()` | Property-based sharding | `entity.ShardBy(e => e.Region)` |
| `ShardByDate<T>()` | Date-based sharding | `entity.ShardByDate(e => e.Date, DateShardInterval.Month)` |
| `ShardByHash<T, K>()` | Hash-based sharding | `entity.ShardByHash(e => e.UserId, shardCount: 8)` |

### Date Shard Intervals
- `DateShardInterval.Day` - Daily partitions
- `DateShardInterval.Month` - Monthly partitions
- `DateShardInterval.Quarter` - Quarterly partitions
- `DateShardInterval.Year` - Yearly partitions
