# DTDE Development Plan - Implementation Phases

[← Back to Testing Strategy](07-testing-strategy.md)

---

## 1. Phase Overview

The DTDE development is organized into 5 phases, following an incremental delivery approach. **Sharding is the primary focus**, with temporal features as an optional add-on.

```
Phase 1: Foundation (4 weeks)
├── Core metadata models
├── Sharding strategies (property-agnostic)
├── Storage mode abstractions
└── Unit test infrastructure

Phase 2: EF Core Integration (4 weeks)
├── DbContext options extension
├── Fluent API extensions (ShardBy, WithStorageMode)
├── Table sharding implementation
└── Integration tests

Phase 3: Query Engine (5 weeks)
├── Shard query planner
├── Parallel query executor
├── Result merger
└── Performance benchmarks

Phase 4: Advanced Features (4 weeks)
├── Database sharding (multi-DB)
├── Manual table support (sqlproj)
├── Optional temporal module
└── Consistency handling

Phase 5: Production Readiness (3 weeks)
├── Documentation
├── Performance optimization
├── NuGet packaging
└── Sample applications

Total: ~20 weeks (5 months)
```

---

## 2. Phase 1: Foundation (Weeks 1-4)

### 2.1 Objectives

- Establish core domain models with **property-agnostic** design
- Create sharding strategy infrastructure
- Implement storage mode abstractions
- Achieve 85% test coverage on core components

### 2.2 Deliverables

| Week | Deliverable | Status |
|------|-------------|--------|
| 1 | Project structure, CI/CD pipeline | 🔲 |
| 1 | `PropertyMetadata`, `EntityMetadata` classes | 🔲 |
| 2 | `ShardingConfiguration`, `ShardStorageMode` enum | 🔲 |
| 2 | `ShardMetadata` with expression-based predicates | 🔲 |
| 3 | `IShardingStrategy` interface and implementations | 🔲 |
| 3 | `PropertyBasedStrategy`, `DateRangeStrategy`, `HashStrategy` | 🔲 |
| 4 | `IShardStorage` interface and `TableShardStorage` | 🔲 |
| 4 | Unit tests for all core components | 🔲 |

### 2.3 Acceptance Criteria

```gherkin
Feature: Sharding Configuration

  Scenario: Configure entity with property-based sharding
    Given an entity type "Customer" with property "Region"
    When I configure sharding using ShardBy(c => c.Region)
    Then the ShardingConfiguration should use PropertyValue strategy
    And the shard key expression should reference "Region"

  Scenario: Configure entity with date-based sharding
    Given an entity type "Order" with property "OrderDate"
    When I configure sharding using ShardByDate(o => o.OrderDate, DateInterval.Year)
    Then the ShardingConfiguration should use DateRange strategy
    And shard tables should be named "Orders_2023", "Orders_2024", etc.

  Scenario: Resolve shards by property value
    Given shards configured for regions "EU", "US", "APAC"
    When I resolve shards for Region = "US"
    Then only "Customers_US" shard should be returned
```

### 2.4 Technical Tasks

```
[ ] Create solution structure
    src/
    ├── Dtde.Core/
    ├── Dtde.EntityFramework/
    └── Dtde.Abstractions/
    tests/
    ├── Dtde.Core.Tests/
    └── Dtde.EntityFramework.Tests/

[ ] Configure Directory.Build.props
    - .NET 8.0 target
    - Nullable enabled
    - Implicit usings
    - Code analysis rules

[ ] Implement PropertyMetadata
    - PropertyName, PropertyType, ColumnName
    - Compiled getters/setters

[ ] Implement ShardingConfiguration
    - ShardKeyExpression (LambdaExpression)
    - StrategyType enum
    - StorageMode enum

[ ] Implement EntityMetadata
    - ClrType, TableName, SchemaName
    - PrimaryKey, Sharding (required)
    - TemporalConfig (optional)

[ ] Implement ShardMetadata
    - ShardId, Name
    - TableName or ConnectionString
    - ShardPredicate (LambdaExpression)

[ ] Implement IShardingStrategy
    - GetShardKey method
    - GetTargetShards method

[ ] Implement PropertyBasedStrategy
    - Simple property value matching
    - Equal/In operators

[ ] Implement DateRangeStrategy
    - Year/Quarter/Month intervals
    - Date range intersection

[ ] Implement HashStrategy
    - Consistent hashing
    - Configurable shard count

[ ] Implement IShardStorage
    - TableShardStorage (same DB)
    - GetShardContext method

[ ] Implement MetadataRegistry
    - Entity registration
    - Shard registration
    - Validation

[ ] Write unit tests (85% coverage)
```

---

## 3. Phase 2: EF Core Integration (Weeks 5-8)

### 3.1 Objectives

- Integrate with EF Core service pipeline
- Implement Fluent API extensions (sharding-first)
- Implement table sharding (single database)
- Enable simple queries to work end-to-end

### 3.2 Deliverables

| Week | Deliverable | Status |
|------|-------------|--------|
| 5 | `DtdeOptionsExtension` for DbContext | 🔲 |
| 5 | `DtdeOptionsBuilder` with fluent configuration | 🔲 |
| 6 | `ShardBy()`, `WithStorageMode()` extensions | 🔲 |
| 6 | `DtdeDbContext` base class | 🔲 |
| 7 | Table name rewriting interceptor | 🔲 |
| 7 | `ShardQueryInterceptor` for query routing | 🔲 |
| 8 | Model finalizer for metadata extraction | 🔲 |
| 8 | Integration tests with table sharding | 🔲 |

### 3.3 Acceptance Criteria

```gherkin
Feature: EF Core Integration

  Scenario: Configure DbContext with DTDE
    Given a DbContext class inheriting from DtdeDbContext
    When I call UseDtde() in OnConfiguring
    Then DTDE services should be registered
    And queries should be intercepted by DTDE

  Scenario: Configure entity with Fluent API
    Given an entity "Customer" in OnModelCreating
    When I call entity.ShardBy(c => c.Region)
    And I call .WithStorageMode(ShardStorageMode.Tables)
    Then EntityMetadata should be created from model
    And tables should be named Customers_EU, Customers_US, etc.

  Scenario: Query routes to single shard
    Given a sharded entity "Customer" with Region = "US"
    When I query db.Customers.Where(c => c.Region == "US").ToListAsync()
    Then only Customers_US table should be queried
    And results should be returned
```

### 3.4 Technical Tasks

```
[ ] Implement DtdeOptionsExtension
    - Implement IDbContextOptionsExtension
    - Register DTDE services
    - Configure service replacements

[ ] Implement DtdeOptionsBuilder
    - ConfigureEntity<T> method
    - SetMaxParallelShards method
    - EnableDiagnostics method

[ ] Implement Fluent API extensions
    - ShardBy<TEntity> extension
    - ShardByDate<TEntity> extension
    - ShardByHash<TEntity> extension
    - WithStorageMode extension

[ ] Implement DtdeDbContext
    - Shard context management
    - Dynamic model caching

[ ] Implement ShardQueryInterceptor
    - Detect shardable queries
    - Route to correct shard table(s)
    - Rewrite table names

[ ] Implement DtdeModelFinalizer
    - Read model annotations
    - Build EntityMetadata from model
    - Register with MetadataRegistry

[ ] Write integration tests (table sharding)
    - Single shard queries
    - Multi-shard queries
    - Insert routing
```

---

## 4. Phase 3: Query Engine (Weeks 9-13)

### 4.1 Objectives

- Implement full distributed query execution
- Support parallel shard queries
- Implement global ordering and pagination
- Achieve performance targets

### 4.2 Deliverables

| Week | Deliverable | Status |
|------|-------------|--------|
| 9 | `DtdeQueryDefinition` model | 🔲 |
| 9 | `ShardQueryPlan` and `ShardQuery` | 🔲 |
| 10 | `IShardQueryPlanner` implementation | 🔲 |
| 10 | Predicate extraction for shard resolution | 🔲 |
| 11 | `IDtdeQueryExecutor` implementation | 🔲 |
| 11 | Parallel execution with bounded concurrency | 🔲 |
| 12 | `IResultMerger` implementation | 🔲 |
| 12 | Global ordering and pagination | 🔲 |
| 13 | Performance benchmarks | 🔲 |
| 13 | Multi-shard integration tests | 🔲 |

### 4.3 Acceptance Criteria

```gherkin
Feature: Distributed Query Execution

  Scenario: Query resolves to single shard
    Given data distributed across 4 quarterly shards
    When I query ValidAt(2024-06-15)
    Then only Shard2024Q2 should be queried
    And results should be returned within 100ms

  Scenario: Query resolves to multiple shards
    Given data distributed across 4 quarterly shards
    When I query ValidBetween(2024-03-01, 2024-07-01)
    Then Shard2024Q1 and Shard2024Q2 should be queried
    And results should be merged correctly

  Scenario: Pagination applies globally
    Given 1000 contracts across 4 shards
    When I query OrderBy(ContractNumber).Skip(10).Take(10)
    Then 10 results should be returned
    And they should be globally sorted by ContractNumber

  Scenario: Performance targets met
    Given 1 million contracts across 4 shards
    When I query ValidAt(date).Take(100)
    Then results should be returned within 200ms
```

### 4.4 Technical Tasks

```
[ ] Implement DtdeQueryDefinition
    - Capture expression tree
    - Extracted shard key predicates
    - Ordering specifications

[ ] Implement ShardQueryPlanner
    - Resolve target shards from predicates
    - Build per-shard query expressions
    - Calculate per-shard take limits

[ ] Implement ParallelQueryExecutor
    - Create per-shard DbContext instances
    - Execute queries in parallel
    - Use SemaphoreSlim for bounded concurrency

[ ] Implement ResultMerger
    - Concatenate shard results
    - Apply global ordering
    - Apply global Skip/Take

[ ] Implement diagnostics
    - ShardResolvedEvent
    - QueryExecutedEvent
    - Per-shard timing

[ ] Create benchmark suite
    - Single shard queries
    - Multi-shard queries
    - Pagination queries
    - Large result set handling

[ ] Write multi-shard integration tests
```

---

## 5. Phase 4: Advanced Features (Weeks 14-17)

### 5.1 Objectives

- Implement database sharding (multiple databases)
- Support manual tables (sqlproj scenarios)
- Add optional temporal module
- Handle cross-shard consistency

### 5.2 Deliverables

| Week | Deliverable | Status |
|------|-------------|--------|
| 14 | `DatabaseShardStorage` implementation | 🔲 |
| 14 | Multi-database connection management | 🔲 |
| 15 | `ManualShardStorage` implementation | 🔲 |
| 15 | Pre-created table routing | 🔲 |
| 16 | Optional `ValidAt()`, `AllVersions()` extensions | 🔲 |
| 16 | `HasTemporalValidity()` Fluent API | 🔲 |
| 17 | `SaveChangesWithVersioningAsync()` method | 🔲 |
| 17 | Cross-shard write consistency | 🔲 |

### 5.3 Acceptance Criteria

```gherkin
Feature: Database Sharding

  Scenario: Route queries to separate databases
    Given Customers_EU on server1 and Customers_US on server2
    When I query db.Customers.Where(c => c.Region == "US").ToListAsync()
    Then only server2 should be connected
    And results from Customers table returned

  Scenario: Manual table configuration
    Given pre-created tables Orders_2023 and Orders_2024
    When I configure UseManualSharding with table predicates
    Then queries should route based on predicates
    And no migrations should be generated

Feature: Optional Temporal Module

  Scenario: Standard update without versioning
    Given a sharded entity without temporal configuration
    When I update and call SaveChangesAsync()
    Then standard EF update should occur (no version bump)

  Scenario: Temporal versioning (opt-in)
    Given a sharded entity WITH temporal configuration
    When I update and call SaveChangesWithVersioningAsync()
    Then old version should be closed (ValidTo = now)
    And new version should be created (ValidFrom = now)

  Scenario: ValidAt query on temporal entity
    Given a temporal entity with multiple versions
    When I query db.Contracts.ValidAt(date).ToListAsync()
    Then only versions valid at that date should be returned
```

### 5.4 Technical Tasks

```
[ ] Implement DatabaseShardStorage
    - Connection string per shard
    - DbContext factory per database
    - Connection pooling

[ ] Implement ManualShardStorage
    - Pre-defined table mappings
    - Predicate-based routing
    - MigrationsEnabled = false support

[ ] Implement temporal query extensions
    - ValidAt<TEntity> method
    - AllVersions<TEntity> method
    - ValidBetween<TEntity> method

[ ] Implement HasTemporalValidity Fluent API
    - Property-agnostic start/end
    - Open-ended validity support

[ ] Implement SaveChangesWithVersioningAsync
    - Explicit opt-in for versioning
    - Close old version + create new
    - Cross-shard version bump

[ ] Implement cross-shard write handling
    - Transaction coordination
    - Best-effort consistency
    - Diagnostic events

[ ] Write advanced integration tests
```

---

## 6. Phase 5: Production Readiness (Weeks 18-20)

### 6.1 Objectives

- Complete documentation
- Optimize performance bottlenecks
- Package for NuGet
- Create sample applications

### 6.2 Deliverables

| Week | Deliverable | Status |
|------|-------------|--------|
| 18 | API documentation (XML comments) | 🔲 |
| 18 | README and getting started guide | 🔲 |
| 19 | Performance profiling and optimization | 🔲 |
| 19 | Connection pool optimization | 🔲 |
| 20 | NuGet package configuration | 🔲 |
| 20 | Sample web API application | 🔲 |

### 6.3 Acceptance Criteria

```gherkin
Feature: Production Readiness

  Scenario: NuGet package installation
    Given a new .NET 8 project
    When I run "dotnet add package Dtde.EntityFramework"
    Then the package should install successfully
    And all dependencies should resolve

  Scenario: Quick start works
    Given the README getting started guide
    When I follow the steps to configure DTDE
    Then I should have a working temporal, sharded application

  Scenario: Performance targets met
    Given the benchmark suite
    When I run all benchmarks
    Then no benchmark should exceed the acceptable threshold
```

### 6.4 Technical Tasks

```
[ ] Complete API documentation
    - XML comments on all public types
    - Examples in remarks
    - Cross-references

[ ] Write README.md
    - Quick start guide
    - Configuration reference
    - FAQ

[ ] Write architecture documentation
    - Component diagrams
    - Flow diagrams
    - Decision records

[ ] Performance optimization
    - Expression caching
    - Connection pooling
    - Compiled delegates

[ ] Configure NuGet packaging
    - Package metadata
    - Symbol packages
    - Source link

[ ] Create sample application
    - Contract management API
    - Multi-shard setup
    - Docker compose
```

---

## 7. Risk Mitigation

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| EF Core internal API changes | High | Medium | Abstract integration points, maintain compatibility tests |
| Performance not meeting targets | High | Medium | Early benchmarking, profiling in Phase 3 |
| Cross-shard consistency issues | High | Medium | Implement outbox pattern, comprehensive testing |
| Complex expression trees failing | Medium | Medium | Extensive expression rewriter testing, fallback paths |
| Integration complexity | Medium | Low | Incremental delivery, early integration tests |

---

## 8. Resource Requirements

### 8.1 Development Team

| Role | Count | Phases |
|------|-------|--------|
| Senior .NET Developer | 2 | All phases |
| EF Core Specialist | 1 | Phases 2-4 |
| QA Engineer | 1 | Phases 3-5 |
| Technical Writer | 0.5 | Phase 5 |

### 8.2 Infrastructure

| Resource | Purpose |
|----------|---------|
| SQL Server instances (3+) | Integration/benchmark testing |
| CI/CD pipeline | Build, test, package automation |
| GitHub repository | Source control, issue tracking |
| NuGet.org account | Package publishing |

---

## 9. Milestone Summary

| Milestone | Week | Key Deliverables |
|-----------|------|-----------------|
| **M1: Foundation Complete** | 4 | Core models, metadata, strategies |
| **M2: Single-Shard Works** | 8 | EF Core integration, temporal queries |
| **M3: Multi-Shard Works** | 13 | Distributed queries, pagination |
| **M4: Updates Work** | 17 | Versioning, cross-shard writes |
| **M5: Release Ready** | 20 | Documentation, NuGet package |

---

## Next Steps

Return to the [Overview](01-overview.md) or explore the [Guides](../guides/index.md) for practical tutorials.
