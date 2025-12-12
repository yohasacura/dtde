# DTDE - Distributed Temporal Data Engine

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![EF Core](https://img.shields.io/badge/EF%20Core-9.0-512BD4)](https://docs.microsoft.com/ef/core/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

A powerful, property-agnostic temporal data engine for .NET applications with Entity Framework Core integration and horizontal sharding support.

## 🚀 Features

- **Temporal Versioning** - Track entity changes over time with configurable validity periods
- **Property-Agnostic** - Use any DateTime properties as temporal boundaries (not limited to `ValidFrom`/`ValidTo`)
- **Point-in-Time Queries** - Query data as it existed at any moment in time
- **Version History** - Access complete change history for any entity
- **Horizontal Sharding** - Distribute data across multiple databases for scalability
- **Multiple Sharding Strategies** - Date-range, hash-based, or composite sharding
- **Fluent Configuration API** - Intuitive builder pattern for configuration
- **EF Core Integration** - Seamless integration with Entity Framework Core 9.0
- **Hot/Warm/Cold Tiering** - Support for data tiering across storage tiers

## 📦 Installation

```bash
# Core abstractions
dotnet add package Dtde.Abstractions

# Core implementations
dotnet add package Dtde.Core

# EF Core integration
dotnet add package Dtde.EntityFramework
```

## 🏁 Quick Start

### 1. Define Your Temporal Entity

```csharp
public class Contract
{
    public int Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    
    // Temporal properties - can be named anything!
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
}
```

### 2. Create Your DbContext

```csharp
public class AppDbContext : DtdeDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    
    public DbSet<Contract> Contracts => Set<Contract>();
}
```

### 3. Configure Services

```csharp
builder.Services.AddDtdeDbContext<AppDbContext>(
    dbOptions => dbOptions.UseSqlServer(connectionString),
    dtdeOptions =>
    {
        dtdeOptions.ConfigureEntity<Contract>(entity =>
        {
            entity.HasTemporalValidity(
                validFrom: nameof(Contract.ValidFrom),
                validTo: nameof(Contract.ValidTo));
        });
    });
```

### 4. Use Temporal Queries

```csharp
public class ContractService
{
    private readonly AppDbContext _context;

    // Get current contracts
    public async Task<List<Contract>> GetCurrentContracts()
    {
        return await _context.ValidAt<Contract>(DateTime.UtcNow).ToListAsync();
    }

    // Get contracts as they existed on a specific date
    public async Task<List<Contract>> GetContractsAsOf(DateTime asOfDate)
    {
        return await _context.ValidAt<Contract>(asOfDate).ToListAsync();
    }

    // Get all versions of a contract
    public async Task<List<Contract>> GetContractHistory(string contractNumber)
    {
        return await _context.AllVersions<Contract>()
            .Where(c => c.ContractNumber == contractNumber)
            .OrderBy(c => c.ValidFrom)
            .ToListAsync();
    }

    // Create a new version of a contract
    public async Task<Contract> UpdateContract(Contract current, Action<Contract> changes, DateTime effectiveDate)
    {
        var newVersion = _context.CreateNewVersion(current, changes, effectiveDate);
        await _context.SaveChangesAsync();
        return newVersion;
    }
}
```

## 🔀 Mixed Usage: Temporal + Regular Entities

DTDE is designed to work seamlessly alongside regular EF Core entities in the same database. Only entities explicitly configured with `HasTemporalValidity()` get temporal behavior.

```csharp
public class AppDbContext : DtdeDbContext
{
    public DbSet<Contract> Contracts => Set<Contract>();      // Temporal entity
    public DbSet<Customer> Customers => Set<Customer>();      // Regular entity
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();      // Regular entity
}

// Only Contract is configured as temporal
dtdeOptions.ConfigureEntity<Contract>(e => 
    e.HasTemporalValidity("ValidFrom", "ValidTo"));
// Customer and AuditLog work as standard EF Core entities
```

```csharp
// TEMPORAL queries (DTDE filtering)
var currentContracts = await _context.ValidAt<Contract>(DateTime.UtcNow).ToListAsync();
var contractHistory = await _context.AllVersions<Contract>().ToListAsync();

// REGULAR EF queries (no temporal filtering)
var customers = await _context.Customers.ToListAsync();
var logs = await _context.AuditLogs.Where(l => l.Date > cutoff).ToListAsync();

// Direct DbSet access bypasses temporal filtering
var allContractRows = await _context.Contracts.ToListAsync();  // ALL rows
var validContracts = await _context.ValidAt<Contract>(now).ToListAsync();  // Filtered
```

| Scenario | Behavior |
|----------|----------|
| Entity NOT configured with `HasTemporalValidity()` | Standard EF Core entity |
| `DbSet<T>` direct access | No temporal filtering |
| `ValidAt<T>()` on non-temporal entity | Returns all rows |
| `ValidAt<T>()` on temporal entity | Applies temporal predicate |

## 📖 Documentation

### Temporal Queries

| Method | Description |
|--------|-------------|
| `ValidAt<T>(date)` | Returns entities valid at a specific point in time |
| `ValidBetween<T>(start, end)` | Returns entities valid within a date range |
| `AllVersions<T>()` | Returns all versions, bypassing temporal filtering |

### Temporal Operations

| Method | Description |
|--------|-------------|
| `AddTemporal<T>(entity, effectiveFrom)` | Adds a new entity with temporal tracking |
| `CreateNewVersion<T>(entity, changes, effectiveDate)` | Creates a new version with changes |
| `Terminate<T>(entity, terminationDate)` | Ends an entity's validity period |

### Entity Configuration

```csharp
// Configure with property names
entity.HasTemporalValidity(
    validFrom: "EffectiveDate",
    validTo: "ExpirationDate");

// Configure with expressions (type-safe)
entity.HasValidity(
    validFromSelector: e => e.EffectiveDate,
    validToSelector: e => e.ExpirationDate);

// Configure primary key (auto-detected by convention)
entity.HasKey(e => e.Id);

// Configure sharding
entity.WithSharding(new DateRangeShardingConfiguration(...));
```

### Sharding Configuration

```csharp
dtdeOptions.AddShard(new ShardMetadataBuilder()
    .WithId("shard-2024")
    .WithName("2024 Data")
    .WithConnectionString("Server=...;Database=Data2024")
    .WithTier(ShardTier.Hot)
    .WithDateRange(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31))
    .Build());

dtdeOptions.AddShard(new ShardMetadataBuilder()
    .WithId("shard-archive")
    .WithName("Archive Data")
    .WithConnectionString("Server=...;Database=Archive")
    .WithTier(ShardTier.Cold)
    .AsReadOnly()
    .Build());
```

### Shard Tiers

| Tier | Description |
|------|-------------|
| `Hot` | Frequently accessed, recent data |
| `Warm` | Less frequently accessed data |
| `Cold` | Archived, rarely accessed data |
| `Archive` | Long-term storage |

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Your Application                          │
├─────────────────────────────────────────────────────────────┤
│                   Dtde.EntityFramework                       │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │ DtdeDbContext│  │Query Engine │  │   Update Engine     │  │
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

## 🧪 Testing

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## 📁 Project Structure

```
Dtde/
├── src/
│   ├── Dtde.Abstractions/     # Core interfaces and contracts
│   ├── Dtde.Core/             # Core implementations
│   └── Dtde.EntityFramework/  # EF Core integration
├── tests/
│   ├── Dtde.Core.Tests/       # Unit tests
│   ├── Dtde.EntityFramework.Tests/
│   └── Dtde.Integration.Tests/
├── samples/
│   └── Dtde.Sample.WebApi/    # Sample REST API
└── docs/
    └── development-plan/      # Design documentation
```

## 🔧 Configuration Options

### DtdeOptionsBuilder

```csharp
services.AddDtde(options =>
{
    // Configure entities
    options.ConfigureEntity<MyEntity>(e => e.HasTemporalValidity("StartDate", "EndDate"));
    
    // Add shards
    options.AddShard(shard => shard.WithId("primary").WithConnectionString("..."));
    
    // Load shards from configuration
    options.AddShardsFromConfig("shards.json");
    
    // Set default temporal context
    options.SetDefaultTemporalContext(() => DateTime.UtcNow);
    
    // Performance tuning
    options.SetMaxParallelShards(10);
    
    // Diagnostics
    options.EnableDiagnostics();
    
    // Testing
    options.EnableTestMode();
});
```

### Shard Configuration File (shards.json)

```json
{
  "shards": [
    {
      "shardId": "shard-2024",
      "name": "2024 Data",
      "connectionString": "Server=...;Database=Data2024",
      "tier": "Hot",
      "dateRangeStart": "2024-01-01",
      "dateRangeEnd": "2024-12-31",
      "isReadOnly": false,
      "priority": 100
    }
  ]
}
```

## 🤝 Contributing

Contributions are welcome! Please read our contributing guidelines before submitting PRs.

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'feat: add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📊 Performance Benchmarks

Comprehensive benchmarks comparing single table vs sharded approaches across various query patterns.

### Benchmark Setup

#### Test Environment
| Component | Specification |
|-----------|---------------|
| **CPU** | 12th Gen Intel Core i9-12900H (14 cores, 20 threads) |
| **Runtime** | .NET 9.0.10, RyuJIT AVX2 |
| **Database** | SQLite (file-based, separate DBs per benchmark) |
| **Framework** | BenchmarkDotNet 0.14.0 |
| **Warmup** | 2 iterations |
| **Iterations** | 5 per benchmark |

#### Data Configuration
| Entity Type | Records | Description |
|-------------|---------|-------------|
| **Customers** | RecordCount / 10 | Base entities with Region sharding |
| **Orders** | RecordCount / 5 | Date-range sharded by OrderDate |
| **Transactions** | RecordCount | High-volume, date-partitioned data |

#### Sharding Strategies Tested
| Strategy | Entity | Shard Key |
|----------|--------|-----------|
| **ShardBy** | Customers | `Region` (US, EU, APAC, etc.) |
| **ShardByDate** | Orders | `OrderDate` (monthly partitions) |
| **ShardByHash** | Transactions | `TransactionDate` + hash distribution |

#### How It Works
- **Single Table:** All data in one SQLite database, standard EF Core queries
- **Sharded:** Data distributed across logical partitions using DTDE's `.UseDtde()` extension
- **Partition Pruning:** Sharded queries automatically target relevant partitions only

### Key Results Summary

#### 🎯 Point Lookups (Primary Key)
| Records | Single Table | Sharded | Difference |
|---------|-------------|---------|------------|
| 10K | 121.6 ns | 125.7 ns | ~Same |
| 50K | 135.2 ns | 134.6 ns | ~Same |
| 100K | 143.9 ns | 146.5 ns | ~Same |

**Insight:** Primary key lookups show nearly identical performance - sharding adds negligible overhead.

#### 📅 Date Range Queries (Single Month)
| Records | Single Table | Sharded | Improvement |
|---------|-------------|---------|-------------|
| 10K | 975 µs | 270 µs | **3.6x faster** |
| 50K | 8,504 µs | 3,156 µs | **2.7x faster** |
| 100K | 16,103 µs | 3,596 µs | **4.5x faster** |

**Insight:** Sharded queries on date-partitioned data show significant speedups due to partition pruning.

#### 🔍 Range Scans (Single Region)
| Records | Single Table | Sharded | Improvement |
|---------|-------------|---------|-------------|
| 10K | 224 µs | 87 µs | **2.6x faster** |
| 50K | 1,023 µs | 301 µs | **3.4x faster** |
| 100K | 3,659 µs | 1,786 µs | **2.0x faster** |

**Insight:** Region-based filtering benefits from shard locality.

#### 📊 Count Operations
| Records | Single Table | Sharded | Improvement |
|---------|-------------|---------|-------------|
| 10K | 24.6 µs | 18.8 µs | **1.3x faster** |
| 50K | 3,534 µs | 26.0 µs | **136x faster** |
| 100K | 7,667 µs | 1,460 µs | **5.3x faster** |

**Insight:** Sharded count operations can be parallelized across partitions.

### When to Use Sharding

Horizontal sharding distributes data across multiple partitions (tables, files, or databases) based on a shard key. This architectural pattern provides benefits in specific scenarios:

#### ✅ Good Candidates for Sharding

| Scenario | Why Sharding Helps |
|----------|-------------------|
| **Large datasets (millions+ rows)** | Reduces per-partition data size, improving query performance |
| **Time-series / temporal data** | Date-based partitioning enables efficient range queries and archival |
| **Multi-tenant applications** | Tenant-based sharding provides data isolation and balanced load |
| **Geographic distribution** | Region-based sharding reduces latency and meets data residency requirements |
| **High write throughput** | Distributes write load across multiple partitions |
| **Hot/Cold data patterns** | Enables tiered storage with recent data on fast storage |

#### ⚠️ When Sharding May Not Help

| Scenario | Consideration |
|----------|--------------|
| **Small datasets (<100K rows)** | Overhead may outweigh benefits |
| **Random access patterns** | Cross-shard queries can be slower than single-table |
| **Complex joins across entities** | Cross-shard joins require careful design |
| **Simple CRUD applications** | Added complexity may not be justified |

#### 🎯 Choosing a Sharding Strategy

| Strategy | Best For | Example |
|----------|----------|---------|
| **Date-based** | Temporal data, logs, events | Partition by month/year |
| **Hash-based** | Even distribution, no natural key | Distribute by hash(id) |
| **Range-based** | Categorical data, regions | Partition by region/tenant |
| **Composite** | Complex access patterns | Date + Region combined |

### Running Benchmarks

```bash
cd benchmarks/Dtde.Benchmarks
dotnet run -c Release

# Run specific benchmark suite
dotnet run -c Release -- --filter "*SingleVsSharded*"

# Quick dry run
dotnet run -c Release -- --filter "*PointLookup*" --job Dry
```



## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- Built with [Entity Framework Core](https://docs.microsoft.com/ef/core/)
- Inspired by temporal database concepts and bi-temporal data modeling
- Designed for modern .NET applications

---

**Made with ❤️ for the .NET community**
