<div align="center">

# DTDE - Distributed Temporal Data Engine

**Transparent horizontal sharding and temporal versioning for Entity Framework Core**

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![EF Core](https://img.shields.io/badge/EF%20Core-9.0-512BD4?style=flat-square)](https://docs.microsoft.com/ef/core/)
[![License](https://img.shields.io/badge/License-MIT-green.svg?style=flat-square)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-101%20passing-success?style=flat-square)](tests/)
[![GitHub](https://img.shields.io/badge/GitHub-yohasacura%2Fdtde-181717?style=flat-square&logo=github)](https://github.com/yohasacura/dtde)

[📚 Documentation](https://yohasacura.github.io/dtde/) · [🚀 Quick Start](#-quick-start) · [💡 Samples](samples/) · [📊 Benchmarks](#-performance-benchmarks)

</div>

---

## Overview

**DTDE** is a NuGet package that adds **transparent horizontal sharding** and **optional temporal versioning** to Entity Framework Core applications. Write standard LINQ queries — DTDE handles data distribution, query routing, and result merging automatically.

```csharp
// Standard EF Core LINQ - DTDE routes queries transparently
var euCustomers = await db.Customers
    .Where(c => c.Region == "EU")
    .ToListAsync();  // Automatically queries only EU shard

// Point-in-time queries for temporal entities
var ordersLastMonth = await db.ValidAt<Order>(DateTime.Today.AddMonths(-1))
    .Where(o => o.Status == "Completed")
    .ToListAsync();
```

---

## ✨ Key Features

| Feature | Description |
|---------|-------------|
| 🔀 **Transparent Sharding** | Distribute data across tables or databases invisibly |
| ⏱️ **Temporal Versioning** | Track entity history with point-in-time queries |
| 🎯 **Property Agnostic** | Use ANY property names for sharding keys and temporal boundaries |
| 📝 **EF Core Native** | Works with standard LINQ — no special query syntax required |
| ⚡ **Multiple Strategies** | Date-based, hash-based, range-based, or composite sharding |
| 🗄️ **Hot/Warm/Cold Tiers** | Support for data tiering across storage tiers |
| ✅ **Fully Tested** | 100+ unit and integration tests |

---

## 📦 Installation

```bash
# All-in-one package (recommended)
dotnet add package Dtde.EntityFramework

# Or install individual packages
dotnet add package Dtde.Abstractions  # Core interfaces
dotnet add package Dtde.Core          # Core implementations
dotnet add package Dtde.EntityFramework  # EF Core integration
```

**Requirements:** .NET 9.0+, Entity Framework Core 9.0+

---

## 🚀 Quick Start

### 1. Define Your Entity

```csharp
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;  // Shard key
    public DateTime CreatedAt { get; set; }
}
```

### 2. Create Your DbContext

```csharp
public class AppDbContext : DtdeDbContext
{
    public DbSet<Customer> Customers => Set<Customer>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ShardBy(c => c.Region);  // Enable sharding by Region
        });
    }
}
```

### 3. Configure Services

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
    options.UseDtde(dtde =>
    {
        dtde.AddShard(s => s.WithId("EU").WithShardKeyValue("EU").WithTier(ShardTier.Hot));
        dtde.AddShard(s => s.WithId("US").WithShardKeyValue("US").WithTier(ShardTier.Hot));
        dtde.AddShard(s => s.WithId("APAC").WithShardKeyValue("APAC").WithTier(ShardTier.Hot));
    });
});
```

### 4. Use It!

```csharp
// Queries are automatically routed to correct shard(s)
var allCustomers = await _context.Customers.ToListAsync();  // Queries all shards
var euCustomers = await _context.Customers
    .Where(c => c.Region == "EU")
    .ToListAsync();  // Only queries EU shard

// Inserts are automatically routed based on shard key
_context.Customers.Add(new Customer { Name = "Acme Corp", Region = "US" });  // Routes to US shard
await _context.SaveChangesAsync();
```

---

## 🔀 Sharding Strategies

DTDE supports multiple sharding strategies to match your data access patterns:

### Property-Based Sharding

Distribute data by a property value (region, tenant, category):

```csharp
modelBuilder.Entity<Customer>(entity =>
{
    entity.ShardBy(c => c.Region);
});
```

**Use cases:** Multi-region deployments, GDPR compliance, data residency

### Date-Based Sharding

Partition data by date for time-series workloads:

```csharp
modelBuilder.Entity<Transaction>(entity =>
{
    entity.ShardByDate(t => t.TransactionDate, DateInterval.Month);
});
```

**Use cases:** Financial transactions, audit logs, metrics, event sourcing

### Hash-Based Sharding

Even distribution across shards using consistent hashing:

```csharp
modelBuilder.Entity<UserProfile>(entity =>
{
    entity.ShardByHash(u => u.UserId, shardCount: 8);
});
```

**Use cases:** High-volume data, preventing hotspots, horizontal scaling

### Composite Sharding

Combine strategies for complex scenarios:

```csharp
modelBuilder.Entity<Order>(entity =>
{
    entity.ShardBy(o => o.Region)
          .ThenByDate(o => o.OrderDate);
});
```

---

## ⏱️ Temporal Versioning

Track entity history and query data at any point in time:

### Enable Temporal Tracking

```csharp
modelBuilder.Entity<Contract>(entity =>
{
    entity.HasTemporalValidity(
        validFrom: nameof(Contract.ValidFrom),
        validTo: nameof(Contract.ValidTo));
});
```

### Temporal Queries

```csharp
// Current data
var current = await _context.ValidAt<Contract>(DateTime.UtcNow).ToListAsync();

// Historical data (as of a specific date)
var historical = await _context.ValidAt<Contract>(new DateTime(2024, 1, 1)).ToListAsync();

// All versions (bypass temporal filtering)
var allVersions = await _context.AllVersions<Contract>()
    .Where(c => c.ContractNumber == "C-001")
    .OrderBy(c => c.ValidFrom)
    .ToListAsync();

// Data within a date range
var rangeData = await _context.ValidBetween<Contract>(startDate, endDate).ToListAsync();
```

### Temporal Operations

```csharp
// Add with effective date
_context.AddTemporal(contract, effectiveFrom: DateTime.UtcNow);

// Create new version (closes old, opens new)
var newVersion = _context.CreateNewVersion(existing, changes, effectiveDate);

// Terminate (close validity)
_context.Terminate(contract, terminationDate: DateTime.UtcNow);

await _context.SaveChangesAsync();
```

---

## 🔄 Mixed Usage: Sharded + Regular Entities

DTDE works seamlessly alongside regular EF Core entities:

```csharp
public class AppDbContext : DtdeDbContext
{
    public DbSet<Customer> Customers => Set<Customer>();   // Sharded
    public DbSet<Contract> Contracts => Set<Contract>();   // Temporal
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();   // Regular EF Core
}
```

| Entity Configuration | Behavior |
|---------------------|----------|
| `ShardBy()` configured | Queries routed by shard key |
| `HasTemporalValidity()` configured | Temporal filtering applied |
| No special configuration | Standard EF Core entity |
| Direct `DbSet<T>` access | Bypasses DTDE filtering |

---

## 🗄️ Shard Tiers

Organize shards by access frequency for cost optimization:

```csharp
dtde.AddShard(s => s
    .WithId("2024-current")
    .WithTier(ShardTier.Hot)         // Frequently accessed, recent data
    .WithConnectionString(fastStorage));

dtde.AddShard(s => s
    .WithId("2023-archive")
    .WithTier(ShardTier.Cold)        // Archived, rarely accessed
    .WithConnectionString(cheapStorage)
    .AsReadOnly());                   // Prevent accidental writes
```

| Tier | Description | Typical Storage |
|------|-------------|-----------------|
| `Hot` | Frequently accessed, recent data | SSD, Premium SQL |
| `Warm` | Less frequently accessed | Standard SQL |
| `Cold` | Archived, rarely accessed | Archive storage |
| `Archive` | Long-term retention | Cold storage |

---

## ⚙️ Configuration Options

### Fluent API

```csharp
options.UseDtde(dtde =>
{
    // Entity configuration
    dtde.ConfigureEntity<Order>(e => e.ShardByDate(o => o.OrderDate));

    // Shard definitions
    dtde.AddShard(s => s.WithId("primary").WithConnectionString("..."));

    // Load from configuration file
    dtde.AddShardsFromConfig("shards.json");

    // Default temporal context
    dtde.SetDefaultTemporalContext(() => DateTime.UtcNow);

    // Performance tuning
    dtde.SetMaxParallelShards(10);

    // Diagnostics
    dtde.EnableDiagnostics();
});
```

### JSON Configuration (shards.json)

```json
{
  "shards": [
    {
      "shardId": "2024-q4",
      "name": "Q4 2024 Data",
      "connectionString": "Server=...;Database=Data2024Q4",
      "tier": "Hot",
      "dateRangeStart": "2024-10-01",
      "dateRangeEnd": "2024-12-31",
      "isReadOnly": false,
      "priority": 100
    },
    {
      "shardId": "2024-archive",
      "name": "2024 Archive",
      "connectionString": "Server=...;Database=Archive2024",
      "tier": "Cold",
      "isReadOnly": true,
      "priority": 10
    }
  ]
}
```

---

## 📊 Performance Benchmarks

Comprehensive benchmarks comparing single table vs sharded approaches:

### Test Environment

| Component | Specification |
|-----------|---------------|
| **CPU** | 12th Gen Intel Core i9-12900H (14 cores, 20 threads) |
| **Runtime** | .NET 9.0, RyuJIT AVX2 |
| **Database** | SQLite (file-based, separate DBs per benchmark) |
| **Framework** | BenchmarkDotNet 0.14.0 |

### Key Results

| Query Type | Records | Single Table | Sharded | Improvement |
|------------|---------|-------------|---------|-------------|
| **Point Lookup** | 100K | 143.9 ns | 146.5 ns | ~Same |
| **Date Range (1 month)** | 100K | 16,103 µs | 3,596 µs | **4.5x faster** |
| **Region Scan** | 100K | 3,659 µs | 1,786 µs | **2.0x faster** |
| **Count** | 50K | 3,534 µs | 26.0 µs | **136x faster** |

**Key insight:** Sharded queries benefit significantly from partition pruning — queries that target a specific shard key value only scan relevant partitions.

### When to Use Sharding

✅ **Good candidates:**
- Large datasets (millions+ rows)
- Time-series / temporal data
- Multi-tenant applications
- Geographic distribution requirements
- High write throughput needs
- Hot/cold data patterns

⚠️ **Consider carefully:**
- Small datasets (<100K rows)
- Random access patterns
- Complex cross-entity joins
- Simple CRUD applications

```bash
# Run benchmarks
cd benchmarks/Dtde.Benchmarks
dotnet run -c Release
```

---

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Your Application                          │
├─────────────────────────────────────────────────────────────┤
│                   Dtde.EntityFramework                       │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │DtdeDbContext│  │Query Engine │  │   Update Engine     │  │
│  │  - ValidAt  │  │ - Rewriter  │  │  - VersionManager   │  │
│  │  - AllVersions│ │ - Executor  │  │  - ShardRouter     │  │
│  └─────────────┘  └─────────────┘  └─────────────────────┘  │
├─────────────────────────────────────────────────────────────┤
│                      Dtde.Core                               │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │  Metadata   │  │  Sharding   │  │     Temporal        │  │
│  │  Registry   │  │  Strategies │  │     Context         │  │
│  └─────────────┘  └─────────────┘  └─────────────────────┘  │
├─────────────────────────────────────────────────────────────┤
│                   Dtde.Abstractions                          │
│         Interfaces • Contracts • Exceptions                  │
└─────────────────────────────────────────────────────────────┘
```

---

## 📁 Project Structure

```
dtde/
├── src/
│   ├── Dtde.Abstractions/        # Core interfaces and contracts
│   ├── Dtde.Core/                # Core implementations
│   └── Dtde.EntityFramework/     # EF Core integration
├── tests/
│   ├── Dtde.Core.Tests/          # Unit tests
│   ├── Dtde.EntityFramework.Tests/
│   └── Dtde.Integration.Tests/
├── samples/                       # Sample applications
│   ├── Dtde.Sample.WebApi/       # Basic getting started
│   ├── Dtde.Samples.RegionSharding/   # Property-based sharding
│   ├── Dtde.Samples.DateSharding/     # Date-based sharding
│   ├── Dtde.Samples.HashSharding/     # Hash-based sharding
│   ├── Dtde.Samples.MultiTenant/      # Multi-tenancy
│   └── Dtde.Samples.Combined/         # Mixed strategies
├── benchmarks/
│   └── Dtde.Benchmarks/          # Performance benchmarks
└── docs/                         # Documentation (MkDocs)
```

---

## 💡 Sample Projects

Explore working examples for each sharding strategy:

| Sample | Strategy | Use Case |
|--------|----------|----------|
| [Dtde.Sample.WebApi](samples/Dtde.Sample.WebApi/) | Basic | Getting started |
| [Dtde.Samples.RegionSharding](samples/Dtde.Samples.RegionSharding/) | Property-based | Multi-region data residency |
| [Dtde.Samples.DateSharding](samples/Dtde.Samples.DateSharding/) | Date-based | Time-series, audit logs |
| [Dtde.Samples.HashSharding](samples/Dtde.Samples.HashSharding/) | Hash-based | Even data distribution |
| [Dtde.Samples.MultiTenant](samples/Dtde.Samples.MultiTenant/) | Tenant-based | SaaS multi-tenancy |
| [Dtde.Samples.Combined](samples/Dtde.Samples.Combined/) | Mixed | Complex enterprise scenarios |

---

## 🧪 Testing

```bash
# Run all tests (101 tests)
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test project
dotnet test tests/Dtde.Core.Tests/
```

---

## 📚 Documentation

- **[Full Documentation](https://yohasacura.github.io/dtde/)** — Complete guides, API reference, and tutorials
- **[Quick Start Guide](docs/guides/quickstart.md)** — Get running in 5 minutes
- **[Sharding Guide](docs/guides/sharding-guide.md)** — Deep dive into sharding strategies
- **[Temporal Guide](docs/guides/temporal-guide.md)** — Temporal versioning explained
- **[API Reference](docs/wiki/api-reference.md)** — Complete API documentation
- **[Architecture](docs/development-plan/00-revised-architecture.md)** — Design decisions and architecture

---

## 🤝 Contributing

Contributions are welcome! Please read our [Contributing Guide](CONTRIBUTING.md) before submitting PRs.

```bash
# Clone and build
git clone https://github.com/yohasacura/dtde.git
cd dtde
dotnet build
dotnet test
```

See also:
- [Code of Conduct](CODE_OF_CONDUCT.md)
- [Security Policy](SECURITY.md)
- [Changelog](CHANGELOG.md)

---

## 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.

---

## 🙏 Acknowledgments

- Built with [Entity Framework Core](https://docs.microsoft.com/ef/core/)
- Inspired by temporal database concepts and bi-temporal data modeling
- Benchmarked with [BenchmarkDotNet](https://benchmarkdotnet.org/)

---

<div align="center">

**Made with ❤️ for the .NET community**

[⭐ Star on GitHub](https://github.com/yohasacura/dtde) · [🐛 Report Bug](https://github.com/yohasacura/dtde/issues) · [💬 Discussions](https://github.com/yohasacura/dtde/discussions)

</div>
