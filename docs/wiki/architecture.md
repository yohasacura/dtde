# Architecture

This document describes the internal architecture of DTDE, its components, and how they interact.

## Table of Contents

- [System Overview](#system-overview)
- [Core Components](#core-components)
- [Query Pipeline](#query-pipeline)
- [Update Pipeline](#update-pipeline)
- [Cross-Shard Transactions](#cross-shard-transactions)
- [Storage Modes](#storage-modes)
- [Extension Points](#extension-points)

---

## System Overview

DTDE is designed as a set of extensions to Entity Framework Core that intercept queries and updates, routing them to appropriate shards transparently.

### High-Level Architecture

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
│  │                 │  │                 │  │                             │ │
│  │ • EntityMeta    │  │ • Shard Resolver│  │ • Shard Write Router        │ │
│  │ • ShardMeta     │  │ • Query Planner │  │ • Version Manager           │ │
│  │ • StrategyMeta  │  │ • Parallel Exec │  │ • Update Processor          │ │
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
│  │  • DtdeOptionsExtension  • Service Replacements                      │   │
│  │  • DtdeDbContext         • Expression Rewriting                      │   │
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
    └───────────────────────────────┘   └───────────────────────────────┘
```

### Design Principles

1. **Transparency**: Application code uses standard EF Core patterns
2. **Property Agnostic**: No assumptions about field names
3. **Sharding First**: Sharding is primary; temporal is optional
4. **Extensibility**: Clean interfaces for custom implementations
5. **Performance**: Parallel execution, minimal overhead

---

## Core Components

### Package Structure

```
Dtde.Abstractions/          # Interfaces and contracts
├── Metadata/
│   ├── IEntityMetadata.cs
│   ├── IShardMetadata.cs
│   ├── IShardRegistry.cs
│   ├── IShardingStrategy.cs
│   └── IMetadataRegistry.cs
└── Temporal/
    └── ITemporalContext.cs

Dtde.Core/                  # Core implementations
├── Metadata/
│   ├── EntityMetadata.cs
│   ├── ShardMetadata.cs
│   ├── ShardRegistry.cs
│   └── MetadataRegistry.cs
├── Sharding/
│   ├── PropertyBasedShardingStrategy.cs
│   ├── HashShardingStrategy.cs
│   └── DateRangeShardingStrategy.cs
└── Temporal/
    └── TemporalContext.cs

Dtde.EntityFramework/       # EF Core integration
├── DtdeDbContext.cs
├── Configuration/
│   ├── DtdeOptions.cs
│   └── DtdeOptionsBuilder.cs
├── Extensions/
│   ├── DbContextOptionsBuilderExtensions.cs
│   ├── EntityTypeBuilderExtensions.cs
│   └── QueryableExtensions.cs
├── Query/
│   ├── ShardedQueryExecutor.cs
│   ├── ShardContextFactory.cs
│   └── DtdeExpressionRewriter.cs
├── Update/
│   ├── ShardWriteRouter.cs
│   ├── DtdeUpdateProcessor.cs
│   └── VersionManager.cs
└── Infrastructure/
    └── DtdeOptionsExtension.cs
```

### Component Responsibilities

#### DtdeDbContext

Base class that extends `DbContext` with DTDE functionality:

```csharp
public abstract class DtdeDbContext : DbContext
{
    // Temporal query methods
    public IQueryable<TEntity> ValidAt<TEntity>(DateTime asOfDate);
    public IQueryable<TEntity> ValidBetween<TEntity>(DateTime start, DateTime end);
    public IQueryable<TEntity> AllVersions<TEntity>();

    // Registry access
    public ITemporalContext TemporalContext { get; }
    public IMetadataRegistry MetadataRegistry { get; }
    public IShardRegistry ShardRegistry { get; }
}
```

#### ShardedQueryExecutor

Executes queries across multiple shards:

```csharp
public class ShardedQueryExecutor : IShardedQueryExecutor
{
    // Execute query across all relevant shards
    Task<IReadOnlyList<T>> ExecuteAsync<T>(IQueryable<T> query, CancellationToken ct);

    // Execute scalar aggregations
    Task<TResult> ExecuteScalarAsync<T, TResult>(IQueryable<T> query,
        Func<IEnumerable<TResult>, TResult> aggregator, CancellationToken ct);
}
```

#### ShardWriteRouter

Routes write operations to correct shards:

```csharp
public class ShardWriteRouter
{
    // Determine target shard for an entity
    IShardMetadata ResolveTargetShard<T>(T entity);

    // Route tracked changes to appropriate shards
    void RouteChanges(IEnumerable<EntityEntry> entries);
}
```

#### ShardRegistry

Maintains collection of available shards:

```csharp
public interface IShardRegistry
{
    IReadOnlyList<IShardMetadata> GetAllShards();
    IShardMetadata? GetShard(string shardId);
    IEnumerable<IShardMetadata> GetShardsForDateRange(DateTime start, DateTime end);
    IEnumerable<IShardMetadata> GetWritableShards();
}
```

---

## Query Pipeline

### Query Execution Flow

```
┌─────────────────┐
│  LINQ Query     │
│  (IQueryable)   │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ Expression      │
│ Analysis        │
│ - Extract where │
│ - Find shard key│
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ Shard Resolution│
│ - Match predicate│
│ - Get target    │
│   shards        │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ Parallel        │
│ Execution       │
│ - Query each    │
│   shard         │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ Result Merging  │
│ - Combine       │
│ - Apply final   │
│   operations    │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ Return Results  │
└─────────────────┘
```

### Shard Resolution

The query executor analyzes the expression tree to determine which shards to query:

```csharp
// Optimized: Only queries EU shard
db.Customers.Where(c => c.Region == "EU")
// Shard resolution: [EU]

// Optimized: Only queries 2024 shard
db.Orders.Where(o => o.CreatedAt.Year == 2024)
// Shard resolution: [2024]

// Cross-shard: Queries all relevant shards
db.Customers.Where(c => c.Name.Contains("Smith"))
// Shard resolution: [EU, US, APAC, ...]
```

### Parallel Execution

Queries execute in parallel with configurable concurrency:

```csharp
// Configuration
options.UseDtde(dtde =>
{
    dtde.SetMaxParallelShards(10);  // Max concurrent queries
});
```

### Result Merging

Results are merged with proper handling of:
- **Ordering**: Re-applies OrderBy across merged results
- **Paging**: Applies Take/Skip to merged results
- **Distinct**: Deduplicates across shards
- **Aggregations**: Combines shard-level aggregates

---

## Update Pipeline

### Write Operation Flow

```
┌─────────────────┐
│ Entity Change   │
│ (Add/Update/    │
│  Delete)        │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ Shard Key       │
│ Extraction      │
│ - Get property  │
│ - Determine key │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ Shard Resolution│
│ - Find matching │
│   shard         │
└────────┬────────┘
         │
         ▼
┌─────────────────┐     ┌─────────────────┐
│ Temporal?       │ Yes │ Version         │
│                 ├────►│ Management      │
└────────┬────────┘     │ - Close old     │
         │ No           │ - Create new    │
         │              └────────┬────────┘
         ▼                       │
┌─────────────────┐              │
│ Direct Write    │              │
│ to Shard        │◄─────────────┘
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ SaveChanges     │
└─────────────────┘
```

### Version Management

For temporal entities, updates create new versions:

```csharp
// Original record
| Id | Amount | ValidFrom  | ValidTo |
| 1  | 10000  | 2024-01-01 | NULL    |

// After update:
| Id | Amount | ValidFrom  | ValidTo    |
| 1  | 10000  | 2024-01-01 | 2024-06-30 |  // Closed
| 2  | 15000  | 2024-07-01 | NULL       |  // New version
```

---

## Cross-Shard Transactions

DTDE provides two-phase commit (2PC) support for operations spanning multiple database shards.

### Transaction Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Application Layer                                  │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  await coordinator.ExecuteInTransactionAsync(async tx => {          │   │
│  │      await tx.EnlistAsync("shard-eu");                               │   │
│  │      await tx.EnlistAsync("shard-us");                               │   │
│  │      // Modify data in both shards                                   │   │
│  │      await context.SaveChangesAsync();                               │   │
│  │  });                                                                 │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                   CrossShardTransactionCoordinator                           │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────────────┐ │
│  │  Transaction    │  │   Phase 1:      │  │      Phase 2:               │ │
│  │  Lifecycle      │  │   PREPARE       │  │      COMMIT/ROLLBACK        │ │
│  │  Management     │  │                 │  │                             │ │
│  └────────┬────────┘  └────────┬────────┘  └──────────────┬──────────────┘ │
│           │                    │                          │                │
└───────────┼────────────────────┼──────────────────────────┼────────────────┘
            │                    │                          │
            ▼                    ▼                          ▼
    ┌───────────────┐    ┌───────────────┐    ┌───────────────┐
    │  Shard EU     │    │  Shard US     │    │  Shard APAC   │
    │  Participant  │    │  Participant  │    │  Participant  │
    │  ┌─────────┐  │    │  ┌─────────┐  │    │  ┌─────────┐  │
    │  │DbContext│  │    │  │DbContext│  │    │  │DbContext│  │
    │  │   +     │  │    │  │   +     │  │    │  │   +     │  │
    │  │ Tx      │  │    │  │ Tx      │  │    │  │ Tx      │  │
    │  └─────────┘  │    │  └─────────┘  │    │  └─────────┘  │
    └───────────────┘    └───────────────┘    └───────────────┘
```

### Two-Phase Commit Protocol

```
Phase 1: PREPARE
┌─────────────┐          ┌─────────────┐          ┌─────────────┐
│ Coordinator │          │  Shard EU   │          │  Shard US   │
└──────┬──────┘          └──────┬──────┘          └──────┬──────┘
       │                        │                        │
       │──── PREPARE ──────────▶│                        │
       │                        │                        │
       │◀─── PREPARED ──────────│                        │
       │                        │                        │
       │──── PREPARE ─────────────────────────────────▶ │
       │                        │                        │
       │◀─── PREPARED ────────────────────────────────── │
       │                        │                        │

Phase 2: COMMIT (if all prepared)
       │                        │                        │
       │──── COMMIT ───────────▶│                        │
       │                        │                        │
       │◀─── COMMITTED ─────────│                        │
       │                        │                        │
       │──── COMMIT ──────────────────────────────────▶ │
       │                        │                        │
       │◀─── COMMITTED ───────────────────────────────── │
       │                        │                        │
```

### Transaction States

```
┌─────────┐     BeginTransaction      ┌──────────┐
│ (None)  │ ─────────────────────────▶│  Active  │
└─────────┘                           └────┬─────┘
                                           │
                    ┌──────────────────────┼──────────────────────┐
                    │                      │                      │
                    ▼ CommitAsync          ▼ RollbackAsync        ▼ DisposeAsync
              ┌───────────┐          ┌───────────┐          ┌───────────┐
              │ Preparing │          │RolledBack │          │RolledBack │
              └─────┬─────┘          └───────────┘          └───────────┘
                    │
        ┌───────────┼───────────┐
        │ Success   │           │ Failure
        ▼           │           ▼
  ┌───────────┐     │     ┌───────────┐
  │ Committed │     │     │  Failed   │
  └───────────┘     │     └───────────┘
                    │
                    ▼ Partial Failure
              ┌───────────┐
              │Recovery   │
              │ Needed    │
              └───────────┘
```

### Component Responsibilities

#### CrossShardTransactionCoordinator

The central coordinator managing transaction lifecycle:

```csharp
public interface ICrossShardTransactionCoordinator
{
    ICrossShardTransaction? CurrentTransaction { get; }

    Task<ICrossShardTransaction> BeginTransactionAsync(
        CrossShardTransactionOptions? options = null,
        CancellationToken ct = default);

    Task ExecuteInTransactionAsync(
        Func<ICrossShardTransaction, Task> action,
        CrossShardTransactionOptions? options = null,
        CancellationToken ct = default);

    Task<int> RecoverAsync(CancellationToken ct = default);
}
```

#### CrossShardTransaction

Represents an active transaction with enlisted participants:

```csharp
public interface ICrossShardTransaction : IAsyncDisposable
{
    string TransactionId { get; }
    TransactionState State { get; }
    IReadOnlyCollection<string> EnlistedShards { get; }

    Task EnlistAsync(string shardId, CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
    ITransactionParticipant? GetParticipant(string shardId);
}
```

#### TransactionParticipant

Each enlisted shard has a participant managing its local transaction:

```csharp
public interface ITransactionParticipant
{
    string ShardId { get; }
    DbContext Context { get; }
    IDbContextTransaction? Transaction { get; }
    ParticipantState State { get; }

    Task PrepareAsync(CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
```

### Transparent Integration

The `TransparentShardingInterceptor` automatically handles cross-shard transactions:

```csharp
// When SaveChanges detects entities targeting multiple shards:
// 1. Identifies all target shards
// 2. If >1 shard and no explicit transaction:
//    - Creates cross-shard transaction
//    - Enlists all shards
//    - Performs two-phase commit
// 3. If explicit transaction active:
//    - Skips automatic handling
//    - Defers to application control
```

---

## Storage Modes

### Table Sharding

Multiple tables in the same database:

```
Database: MyAppDb
├── Customers_EU
├── Customers_US
├── Customers_APAC
├── Orders_2023
├── Orders_2024
└── Orders_2025
```

**Advantages:**
- Single connection string
- Simpler deployment
- Easier transactions

**Configuration:**
```csharp
entity.ShardBy(c => c.Region)
      .WithStorageMode(ShardStorageMode.Tables);
```

### Database Sharding

Separate databases (potentially on different servers):

```
Server: EU-Server
└── Database: MyAppEU
    └── Customers

Server: US-Server
└── Database: MyAppUS
    └── Customers

Server: APAC-Server
└── Database: MyAppAPAC
    └── Customers
```

**Advantages:**
- True horizontal scaling
- Data isolation
- Geographic distribution

**Configuration:**
```csharp
entity.ShardBy(c => c.Region)
      .WithStorageMode(ShardStorageMode.Databases);

dtde.AddShard(s => s
    .WithId("EU")
    .WithConnectionString("Server=EU-Server;Database=MyAppEU;..."));
```

---

## Extension Points

### Custom Sharding Strategy

Implement `IShardingStrategy` for custom logic:

```csharp
public interface IShardingStrategy
{
    string StrategyType { get; }
    IEnumerable<IShardMetadata> ResolveShards(Expression predicate, IShardRegistry registry);
    IShardMetadata ResolveWriteShard(object entity, IShardRegistry registry);
}
```

### Custom Shard Context Factory

Implement `IShardContextFactory` for custom DbContext creation:

```csharp
public interface IShardContextFactory
{
    Task<DbContext> CreateContextAsync(IShardMetadata shard, CancellationToken ct);
}
```

### Custom Expression Rewriter

Implement `IExpressionRewriter` for custom query transformation:

```csharp
public interface IExpressionRewriter
{
    Expression Rewrite(Expression expression, IShardMetadata shard);
}
```

---

## Performance Considerations

### Query Optimization

1. **Shard Pruning**: Include shard key in WHERE clauses
2. **Parallel Limits**: Configure `MaxParallelShards` appropriately
3. **Index Strategy**: Index shard key columns

### Memory Management

1. **Streaming**: Use `AsAsyncEnumerable()` for large result sets
2. **Pagination**: Implement proper paging for large datasets
3. **Projection**: Use `Select()` to reduce data transfer

### Connection Pooling

For database sharding, each database has its own connection pool:

```csharp
// Configure per-shard connection pooling
dtde.AddShard(s => s
    .WithConnectionString("...;Max Pool Size=100;..."));
```

---

## Next Steps

- [API Reference](api-reference.md) - Complete API documentation
- [Configuration](configuration.md) - Configuration options
- [Classes Reference](classes-reference.md) - Detailed class docs

---

[← Back to Wiki](index.md) | [API Reference →](api-reference.md)
