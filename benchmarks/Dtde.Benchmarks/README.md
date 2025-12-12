# DTDE Comprehensive Benchmarks

This benchmark suite provides comprehensive performance comparisons between traditional single-table approaches and DTDE's sharding strategies.

## 📊 Benchmark Suites

### 1. Single Table vs Sharded Table Comparison (`SingleVsShardedBenchmarks`)
Compares basic operations between non-sharded and sharded approaches.

**Test Categories:**
- **PointLookup**: Single record retrieval by ID
- **RangeScan**: Retrieving multiple records by a range filter
- **Aggregation**: Sum, count, and average operations
- **DateQuery**: Date-based filtering and retrieval
- **Count**: Simple count operations

**Data Sizes:** 10,000 | 100,000 | 1,000,000 records

### 2. Indexed vs Non-Indexed Fields (`IndexedFieldsBenchmarks`)
Demonstrates the performance impact of database indexes.

**Test Categories:**
- **NonIndexedFilter**: Filtering on non-indexed columns
- **IndexedFilter**: Filtering on indexed columns
- **DateRangeQuery**: Date range queries with/without indexes
- **CompoundQuery**: Multi-column filtering
- **UniqueLookup**: Unique constraint lookups
- **OrderBy**: Sorting operations
- **AggregationWithFilter**: Aggregations with WHERE clauses

**Data Sizes:** 100,000 | 500,000 records

### 3. Join and Include Operations (`JoinIncludeBenchmarks`)
Evaluates the performance of related data loading.

**Test Categories:**
- **SimpleInclude**: Single-level `.Include()` operations
- **MultiLevelInclude**: Nested `.ThenInclude()` chains
- **ExplicitJoin**: LINQ `join` syntax
- **MultipleJoins**: Three-way joins
- **JoinWithAggregation**: Joins combined with GroupBy
- **LeftJoin**: Left outer join patterns
- **FilteredInclude**: `.Include()` with filtered navigation

**Customer Counts:** 10,000 | 50,000 customers

### 4. Nested Properties and Navigation (`NestedPropertiesBenchmarks`)
Tests deep object graph traversal.

**Test Categories:**
- **SingleLevelNavigation**: Customer → Profile
- **DeepNavigation**: Customer → Profile → Preferences
- **ProductDetailsNavigation**: Product → Details
- **ProductAttributesNavigation**: Product → Details → Attributes
- **FullObjectGraph**: Loading complete entity graphs
- **NestedProjection**: Projecting nested properties to DTOs
- **FilterByNestedProperty**: Filtering by child properties
- **NestedAggregation**: Aggregating nested values
- **OrderByNested**: Sorting by child properties

**Entity Counts:** 5,000 | 25,000 entities

### 5. Write Operations (`WriteOperationsBenchmarks`)
Compares insert, update, and delete performance.

**Test Categories:**
- **BatchInsert**: Bulk insert operations
- **SingleInsert**: One-by-one inserts
- **BatchUpdate**: Bulk updates via LINQ
- **BatchDelete**: Bulk delete operations
- **CascadingInsert**: Inserting parent + children
- **Upsert**: Insert-or-update patterns
- **ExecuteUpdate**: EF Core 7+ bulk update
- **ExecuteDelete**: EF Core 7+ bulk delete

**Batch Sizes:** 100 | 1,000 | 5,000 records

### 6. Concurrent Access Patterns (`ConcurrentAccessBenchmarks`)
Evaluates parallel access performance.

**Test Categories:**
- **ParallelReadsSameRegion**: Multiple readers on same data
- **ParallelReadsDifferentRegions**: Readers on separate shards
- **ParallelAggregation**: Concurrent aggregations
- **ParallelComplexQuery**: Complex queries in parallel
- **MixedWorkload**: 80% reads / 20% writes
- **HotSpot**: All tasks accessing same record
- **ScatterGather**: Query all shards + merge results
- **ReadAfterWrite**: Write-then-read consistency

**Parallel Tasks:** 4 | 8 | 16 threads
**Record Counts:** 10,000 | 50,000 records

## 🚀 Running the Benchmarks

### Interactive Mode
```bash
cd benchmarks/Dtde.Benchmarks
dotnet run -c Release
```

Then select a benchmark suite (1-7).

### Command Line
```bash
# Run all benchmarks
dotnet run -c Release --filter "*"

# Run specific benchmark
dotnet run -c Release --filter "SingleVsShardedBenchmarks"

# Run with specific category
dotnet run -c Release --filter "*PointLookup*"
```

### Output
Results are exported to:
- `BenchmarkDotNet.Artifacts/results/` - Markdown and CSV reports
- Console output with tables

## 📈 What the Benchmarks Measure

| Metric | Description |
|--------|-------------|
| Mean | Average execution time |
| StdDev | Standard deviation (consistency) |
| Median | Middle value (less affected by outliers) |
| P95 | 95th percentile (worst-case performance) |
| Op/s | Operations per second |
| Allocated | Memory allocated per operation |
| Gen 0/1/2 | Garbage collection per 1000 ops |

## 🏗️ Sharding Strategies Tested

1. **Property-Based Sharding** (`ShardBy`)
   - Customers, Orders sharded by `Region`
   - Co-located related entities

2. **Date-Based Sharding** (`ShardByDate`)
   - Transactions sharded by `TransactionDate` (monthly)
   - Optimal for time-series data

3. **Hash-Based Sharding** (`ShardByHash`)
   - Products distributed across 8 shards by `Id`
   - Even distribution for high-cardinality data

## 📝 Entity Relationships

```
Customer (Region-sharded)
├── CustomerProfile (co-located by Region)
│   └── CustomerPreferences
└── Order (co-located by Region)
    └── OrderItem (co-located by Region)

Product (Hash-sharded by Id)
└── ProductDetails
    └── ProductAttribute[]

Transaction (Date-sharded by Month)
```

## 💡 Expected Insights

1. **Sharding Benefits**
   - Improved query performance when shard key is in WHERE clause
   - Better parallel execution across shards
   - Reduced contention for concurrent access

2. **Sharding Costs**
   - Scatter-gather queries (no shard key) may be slower
   - Cross-shard joins require careful design
   - Insert routing adds minimal overhead

3. **Index Impact**
   - Indexes significantly improve filter/sort operations
   - Index maintenance affects write performance
   - Compound indexes benefit multi-column queries

4. **Concurrency Patterns**
   - Shard-local operations scale linearly
   - Hot spots cause contention regardless of approach
   - Mixed workloads benefit from shard isolation

## 🔧 Configuration

The benchmarks use SQLite databases for portability. Each benchmark creates isolated database files that are cleaned up after execution.

For production-like results, consider:
- Using SQL Server or PostgreSQL
- Running on dedicated hardware
- Increasing data sizes
- Warming up the database cache
