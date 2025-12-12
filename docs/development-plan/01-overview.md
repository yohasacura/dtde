# DTDE Development Plan - Overview

## Document Navigation

| Part | Title | Description |
|------|-------|-------------|
| **00** | [Revised Architecture](00-revised-architecture.md) | **NEW**: Clarified design principles and separation of concerns |
| **01** | [Overview](01-overview.md) | Project vision, goals, and high-level architecture |
| **01a** | [Sharding Strategies](01a-sharding-strategies.md) | **NEW**: Property-agnostic sharding options |
| **01b** | [Temporal Versioning](01b-temporal-versioning.md) | **NEW**: Optional temporal features |
| **02** | [Core Domain Model](02-core-domain-model.md) | Metadata, entities, and domain abstractions |
| **03** | [EF Core Integration](03-ef-core-integration.md) | Query pipeline, interceptors, and DbContext |
| **04** | [Query Engine](04-query-engine.md) | Shard resolution, parallel execution, result merging |
| **05** | [Update Engine](05-update-engine.md) | Temporal versioning, write pipeline |
| **06** | [Configuration & API](06-configuration-api.md) | Fluent API, DI extensions, developer experience |
| **07** | [Testing Strategy](07-testing-strategy.md) | Test plan, scenarios, and quality gates |
| **08** | [Implementation Phases](08-implementation-phases.md) | Milestones and delivery timeline |

---

## 1. Executive Summary

**Distributed Temporal Data Engine (DTDE)** is a NuGet package that provides **transparent horizontal sharding** and **optional temporal versioning** on top of Entity Framework Core. The library allows developers to work with standard EF Core patterns while the engine handles:

- **Transparent Sharding** - Distribute data across tables or databases (primary feature)
- **Optional Temporal Validity** - Track entity versions over time (opt-in feature)
- **Property-Agnostic Configuration** - Use ANY property names for sharding and temporal boundaries
- **Multiple Storage Modes** - Same database (table sharding) or separate databases
- **Manual Table Support** - Works with pre-created tables (sqlproj, DacPac)
- **Standard EF Behavior** - Non-configured entities work exactly like regular EF Core

### Key Design Principles

> **1. Sharding First**: DTDE is primarily a sharding library. Temporal versioning is optional.
>
> **2. Property Agnostic**: No hardcoded property names. Configure any properties for sharding or temporal boundaries.
>
> **3. EF Core Compatible**: Write standard LINQ. DTDE handles distribution transparently.
>
> **4. Opt-In Features**: Only entities you configure get DTDE behavior. Others remain standard EF Core.

---

## 2. Project Goals

### 2.1 Primary Goals (Sharding)

| Goal | Description | Success Criteria |
|------|-------------|------------------|
| **Transparent Sharding** | DbSet appears as single collection | No shard-aware code in application |
| **Property-Agnostic** | Shard by ANY property | `ShardBy(c => c.Region)`, `ShardBy(c => c.Year)` |
| **Multiple Strategies** | Hash, Range, Date, Alphabetic, Row Count | All strategies configurable |
| **Storage Modes** | Tables in same DB or separate databases | Configurable per entity |
| **Manual Tables** | Support pre-created tables | sqlproj/DacPac compatibility |
| **Performance** | Sub-200ms for typical queries | Parallel shard execution |

### 2.2 Secondary Goals (Temporal - Optional)

| Goal | Description | Success Criteria |
|------|-------------|------------------|
| **Optional Temporal** | Opt-in per entity | Only configured entities versioned |
| **Property-Agnostic** | Any DateTime property names | `HasTemporalValidity(e => e.StartDate, e => e.EndDate)` |
| **Point-in-Time Queries** | Query as of any date | `ValidAt(date)` method |
| **Version History** | Access all versions | `AllVersions()` method |

### 2.3 Non-Goals (v1)

- Cross-database distributed transactions
- Non-SQL Server databases
- Real-time synchronization between shards
- Forced temporal versioning on all entities

---

## 3. High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Application Layer                                  │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  // Standard EF Core LINQ - no shard-aware code needed              │   │
│  │  var result = await db.Orders                                        │   │
│  │      .Where(o => o.Region == "EU" && o.Status == "Pending")          │   │
│  │      .OrderBy(o => o.OrderDate)                                      │   │
│  │      .Take(10)                                                       │   │
│  │      .ToListAsync();                                                 │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                              DTDE NuGet Package                              │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────────────┐ │
│  │ Metadata Layer  │  │  Query Engine   │  │      Update Engine          │ │
│  │                 │  │  (ALWAYS)       │  │      (SHARDING)             │ │
│  │ • EntityMeta    │  │ • Shard Resolver│  │ • Shard Write Router        │ │
│  │ • ShardMeta     │  │ • Query Planner │  │ • Insert/Update/Delete      │ │
│  │ • StrategyMeta  │  │ • Parallel Exec │  │                             │ │
│  └────────┬────────┘  │ • Result Merger │  └──────────────┬──────────────┘ │
│           │           └────────┬────────┘                 │                │
│           │                    │                          │                │
│  ┌────────┴────────────────────┴──────────────────────────┴────────────┐   │
│  │                  Optional: Temporal Module                           │   │
│  │  • ValidAt() / ValidBetween() / AllVersions()                        │   │
│  │  • Version bump on update (if configured)                            │   │
│  │  • Temporal Include for relationships                                │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                      EF Core Integration Layer                       │   │
│  │  • IQueryTranslationPostprocessor  • SaveChangesInterceptor          │   │
│  │  • Custom DbContext (DtdeDbContext) • Service Replacements           │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                    ┌─────────────────┴─────────────────┐
                    ▼                                   ▼
    ┌───────────────────────────────┐   ┌───────────────────────────────┐
    │   Table Sharding (Same DB)    │   │  Database Sharding (Multi-DB) │
    │  ┌───────┐ ┌───────┐ ┌─────┐ │   │  ┌───────┐ ┌───────┐ ┌─────┐ │
    │  │Tbl_EU │ │Tbl_US │ │Tbl_X│ │   │  │ DB_EU │ │ DB_US │ │DB_X │ │
    │  └───────┘ └───────┘ └─────┘ │   │  └───────┘ └───────┘ └─────┘ │
    │  (Orders_EU, Orders_US, etc) │   │  (Separate SQL Server DBs)    │
    └───────────────────────────────┘   └───────────────────────────────┘
```

---

## 4. Core Concepts

### 4.1 Sharding (Primary Feature)

DTDE supports **any property** as a shard key:

```csharp
// Shard by Region (property-based)
modelBuilder.Entity<Customer>()
    .ShardBy(c => c.Region);

// Shard by Year (date-based)
modelBuilder.Entity<Order>()
    .ShardByDate(o => o.OrderDate, DateShardInterval.Year);

// Shard by ID (hash-based, even distribution)
modelBuilder.Entity<Product>()
    .ShardByHash(p => p.Id, shardCount: 8);

// Shard by first letter (alphabetic)
modelBuilder.Entity<Contact>()
    .ShardByAlphabet(c => c.LastName, new[] { "A-M", "N-Z" });

// Shard by row count (auto-rotate)
modelBuilder.Entity<LogEntry>()
    .ShardByRowCount(maxRows: 1_000_000);

// Custom expression
modelBuilder.Entity<Transaction>()
    .ShardBy(t => t.Amount > 10000 ? "HighValue" : "Standard");
```

### 4.2 Storage Modes

```csharp
// Table sharding (same database)
entity.ShardBy(c => c.Region)
      .WithStorageMode(ShardStorageMode.Tables);
// Creates: Customers_EU, Customers_US, Customers_APAC (in same DB)

// Database sharding (separate databases)
entity.ShardBy(c => c.Region)
      .WithStorageMode(ShardStorageMode.Databases)
      .AddDatabase("EU", euConnectionString)
      .AddDatabase("US", usConnectionString);
// Uses: EU_Server.Customers, US_Server.Customers

// Manual tables (pre-created, sqlproj)
entity.UseManualSharding(config =>
{
    config.AddTable("dbo.Orders_2023", o => o.Year == 2023);
    config.AddTable("dbo.Orders_2024", o => o.Year == 2024);
    config.MigrationsEnabled = false;
});
```

### 4.3 Temporal Validity (Optional)

DTDE supports **any property names** for temporal boundaries:

```csharp
// Standard naming
modelBuilder.Entity<Contract>()
    .HasTemporalValidity(c => c.ValidFrom, c => c.ValidTo);

// Domain-specific naming
modelBuilder.Entity<Policy>()
    .HasTemporalValidity(p => p.EffectiveDate, p => p.ExpirationDate);

// Single boundary (open-ended)
modelBuilder.Entity<Subscription>()
    .HasTemporalValidity(s => s.StartDate); // No end date = perpetual validity
```

### 4.4 Temporal Queries (When Configured)

```csharp
// Query-level temporal filter
var activeContracts = await db.Contracts
    .ValidAt(DateTime.Today)
    .ToListAsync();

// Historical data access
var history = await db.Contracts
    .AllVersions()
    .Where(c => c.Id == contractId)
    .ToListAsync();

// Non-temporal entities - ValidAt returns all (no filtering)
var customers = await db.Customers.ValidAt(DateTime.Today).ToListAsync();
// Same as: await db.Customers.ToListAsync()
```

---

## 5. Technology Stack

| Component | Technology | Justification |
|-----------|------------|---------------|
| Runtime | .NET 8+ | LTS, performance, modern C# features |
| ORM | EF Core 8+ | Industry standard, extensible pipeline |
| Database | SQL Server | Target platform (extensible for others) |
| Logging | Microsoft.Extensions.Logging | Standard .NET logging abstraction |
| DI | Microsoft.Extensions.DependencyInjection | Built-in .NET DI |
| Testing | xUnit + FluentAssertions + Moq | Industry standard for .NET |
| Benchmarking | BenchmarkDotNet | Performance validation |

---

## 6. Package Structure

```
DTDE/
├── src/
│   ├── Dtde.Core/                          # Core abstractions and domain
│   │   ├── Metadata/
│   │   │   ├── IEntityMetadata.cs
│   │   │   ├── EntityMetadata.cs
│   │   │   ├── IShardMetadata.cs
│   │   │   ├── ShardMetadata.cs
│   │   │   ├── ITemporalMetadata.cs         # Optional temporal config
│   │   │   └── IMetadataRegistry.cs
│   │   ├── Sharding/
│   │   │   ├── IShardResolver.cs
│   │   │   ├── IShardingStrategy.cs
│   │   │   ├── Strategies/
│   │   │   │   ├── PropertyBasedStrategy.cs   # Simple property value matching
│   │   │   │   ├── DateRangeStrategy.cs       # Date-based ranges
│   │   │   │   ├── HashStrategy.cs            # Hash distribution
│   │   │   │   ├── AlphabetStrategy.cs        # First-letter ranges
│   │   │   │   ├── MaxRowsStrategy.cs         # Row count rotation
│   │   │   │   └── CompositeStrategy.cs       # Multiple properties
│   │   │   └── Storage/
│   │   │       ├── IShardStorage.cs           # Storage abstraction
│   │   │       ├── TableShardStorage.cs       # Same DB, multiple tables
│   │   │       ├── DatabaseShardStorage.cs    # Separate databases
│   │   │       └── ManualShardStorage.cs      # Pre-created tables (sqlproj)
│   │   ├── Temporal/                        # Optional temporal module
│   │   │   ├── ITemporalContext.cs
│   │   │   ├── TemporalContext.cs
│   │   │   └── ValidityPeriod.cs
│   │   └── Common/
│   │       ├── Pagination/
│   │       └── Exceptions/
│   │
│   ├── Dtde.EntityFramework/               # EF Core integration
│   │   ├── Extensions/
│   │   │   ├── ServiceCollectionExtensions.cs
│   │   │   ├── EntityTypeBuilderExtensions.cs
│   │   │   └── QueryableExtensions.cs
│   │   ├── Query/
│   │   │   ├── ShardQueryInterceptor.cs      # Main query interceptor
│   │   │   ├── ShardQueryPlanner.cs          # Determines target shards
│   │   │   ├── ParallelQueryExecutor.cs      # Multi-shard execution
│   │   │   └── ResultMerger.cs               # Aggregates results
│   │   ├── Update/
│   │   │   ├── ShardSaveChangesInterceptor.cs
│   │   │   ├── ShardWriteRouter.cs           # Routes writes to correct shard
│   │   │   └── VersionManager.cs             # Optional: temporal versioning
│   │   ├── Context/
│   │   │   ├── DtdeDbContext.cs
│   │   │   └── DynamicModelCache.cs          # Caches per-shard models
│   │   └── Configuration/
│   │       ├── DtdeOptions.cs
│   │       ├── DtdeOptionsBuilder.cs
│   │       ├── ShardConfiguration.cs
│   │       └── ManualTableConfiguration.cs   # For sqlproj scenarios
│   │
│   └── Dtde.SqlServer/                     # SQL Server specific
│       ├── SqlServerShardConnection.cs
│       └── SqlServerQueryBuilder.cs
│
├── tests/
│   ├── Dtde.Core.Tests/
│   ├── Dtde.EntityFramework.Tests/
│   ├── Dtde.Integration.Tests/
│   └── Dtde.Benchmarks/
│
├── samples/
│   └── Dtde.Sample.WebApi/
│
└── docs/
    └── development-plan/
```

---

## 7. DDD Analysis

### 7.1 Bounded Contexts

| Context | Responsibility |
|---------|----------------|
| **Metadata** | Entity, shard, and temporal configuration management |
| **Query** | LINQ interception, shard resolution, parallel execution, result merging |
| **Update** | Change tracking interception, shard routing, optional versioning |
| **Sharding** | Strategy evaluation, storage mode management, connection routing |

### 7.2 Aggregates

| Aggregate | Root Entity | Invariants |
|-----------|-------------|------------|
| `EntityConfiguration` | `EntityMetadata` | Must have valid key property, shard config optional |
| `ShardRegistry` | `ShardMetadata` | Valid connection strings, non-overlapping ranges/predicates |
| `StorageConfiguration` | `ShardStorageConfig` | Valid table names or connection strings per mode |
| `QueryPlan` | `ShardQueryPlan` | At least one shard must be targeted |

### 7.3 Domain Events

| Event | Trigger | Handlers |
|-------|---------|----------|
| `EntityConfigured` | Fluent API configuration complete | MetadataRegistry |
| `ShardResolved` | Query planning complete | Logging, Diagnostics |
| `ShardCreated` | New shard table/DB created (auto-mode) | Schema Manager |
| `VersionCreated` | New temporal version saved (opt-in) | Audit, Logging |

---

## 8. SOLID Principles Application

### 8.1 Single Responsibility Principle

| Class | Single Responsibility |
|-------|----------------------|
| `MetadataRegistry` | Store and retrieve entity/shard metadata |
| `ShardResolver` | Determine which shards to query based on predicates |
| `ShardQueryPlanner` | Plan query execution across resolved shards |
| `ParallelQueryExecutor` | Execute queries across shards concurrently |
| `ResultMerger` | Combine and paginate results from multiple shards |
| `ShardWriteRouter` | Route inserts/updates/deletes to correct shard |
| `VersionManager` | Handle temporal version creation (optional feature) |

### 8.2 Open/Closed Principle

```csharp
// Extensible sharding strategies without modifying core code
public interface IShardingStrategy
{
    string GetShardKey<T>(T entity, Expression<Func<T, object>> shardProperty);
    IEnumerable<string> GetTargetShards(Expression predicate);
}

// Extensible storage modes
public interface IShardStorage
{
    DbContext GetShardContext(string shardKey);
    Task<IEnumerable<DbContext>> GetAllShardContextsAsync();
    bool SupportsAutoCreate { get; }
}

// New strategies/modes can be added without changing core code
public class CustomShardingStrategy : IShardingStrategy { ... }
public class CosmosDbShardStorage : IShardStorage { ... }
```

### 8.3 Dependency Inversion Principle

```csharp
// High-level query planner depends on abstractions
public class ShardQueryPlanner
{
    private readonly IShardResolver _shardResolver;
    private readonly IShardStorage _shardStorage;
    private readonly ILogger<ShardQueryPlanner> _logger;

    public ShardQueryPlanner(
        IShardResolver shardResolver,
        IShardStorage shardStorage,
        ILogger<ShardQueryPlanner> logger)
    {
        _shardResolver = shardResolver;
        _shardStorage = shardStorage;
        _logger = logger;
    }
}
```

---

## 9. Quality Gates

| Gate | Threshold | Enforcement |
|------|-----------|-------------|
| Unit Test Coverage | ≥ 85% (Domain & Application layers) | CI pipeline |
| Integration Test Pass Rate | 100% | CI pipeline |
| Performance Benchmark | < 200ms for standard queries | Release gate |
| Code Quality | No critical/high Sonar issues | PR merge gate |
| Documentation | All public APIs documented | PR review |

---

## 10. Risk Assessment

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| EF Core internal API changes | Medium | High | Version-specific adapters, extensive integration tests |
| Cross-shard consistency failures | Medium | High | Idempotent operations, compensation patterns, outbox |
| Performance degradation with many shards | Low | Medium | Bounded parallelism, connection pooling, caching |
| Complex expression trees not supported | Medium | Medium | Comprehensive test suite, graceful fallback |

---

## Next Steps

Continue to [02 - Core Domain Model](02-core-domain-model.md) for detailed domain analysis and metadata structures.
