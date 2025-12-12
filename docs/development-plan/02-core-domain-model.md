# DTDE Development Plan - Core Domain Model

[← Back to Overview](01-overview.md) | [Next: EF Core Integration →](03-ef-core-integration.md)

---

## 1. Domain Analysis

### 1.1 Ubiquitous Language

| Term | Definition |
|------|------------|
| **Entity Metadata** | Configuration describing a CLR entity: table mapping, key properties, sharding, and optional temporal configuration |
| **Shard** | A logical partition of data, stored as either a separate table (same DB) or separate database |
| **Shard Key** | A property or expression used to determine which shard an entity belongs to |
| **Shard Resolution** | The process of determining which shards to query based on predicates |
| **Storage Mode** | How shards are physically stored: `Tables` (same DB), `Databases` (separate), or `Manual` (pre-created) |
| **Validity Period** | (Optional) A time range during which a temporal record is considered "active" |
| **Temporal Context** | (Optional) The point-in-time used to filter temporal entities in queries |
| **Version Bump** | (Optional) Creating a new version while closing the validity of the previous version |
| **Manual Table** | A pre-created table (e.g., via sqlproj) that DTDE routes to without creating migrations |

### 1.2 Domain Invariants

1. **Entity metadata must have a valid primary key property**
2. **Shard predicates/ranges must not overlap for the same entity**
3. **Manual table names must exist in the database (no auto-creation)**
4. **If temporal, entity must have at least one validity property (start date)**
5. **Database shard storage requires valid connection strings for each shard**

---

## 2. Metadata Layer

### 2.1 Entity Metadata

The `EntityMetadata` aggregate captures all configuration for a single entity type.

```csharp
namespace Dtde.Core.Metadata;

/// <summary>
/// Represents metadata configuration for a temporal and sharded entity.
/// </summary>
public sealed class EntityMetadata
{
    /// <summary>
    /// Gets the CLR type of the entity.
    /// </summary>
    public Type ClrType { get; }
    
    /// <summary>
    /// Gets the database table name.
    /// </summary>
    public string TableName { get; }
    
    /// <summary>
    /// Gets the schema name (default: "dbo").
    /// </summary>
    public string SchemaName { get; }
    
    /// <summary>
    /// Gets the primary key property configuration.
    /// </summary>
    public PropertyMetadata PrimaryKey { get; }
    
    /// <summary>
    /// Gets the optional validity period configuration.
    /// Null if entity is not temporal.
    /// </summary>
    public ValidityConfiguration? Validity { get; }
    
    /// <summary>
    /// Gets the optional sharding configuration.
    /// Null if entity is not sharded.
    /// </summary>
    public ShardingConfiguration? Sharding { get; }
    
    /// <summary>
    /// Gets whether this entity supports temporal queries.
    /// </summary>
    public bool IsTemporal => Validity is not null;
    
    /// <summary>
    /// Gets whether this entity is distributed across shards.
    /// </summary>
    public bool IsSharded => Sharding is not null;
}
```

### 2.2 Validity Configuration (Property Agnostic)

The key design decision: **validity properties are customer-configurable**.

```csharp
namespace Dtde.Core.Metadata;

/// <summary>
/// Configuration for temporal validity properties.
/// Property names are fully configurable by the customer.
/// </summary>
/// <example>
/// <code>
/// // Standard naming
/// new ValidityConfiguration(
///     validFromProperty: "ValidFrom",
///     validToProperty: "ValidTo");
/// 
/// // Domain-specific naming
/// new ValidityConfiguration(
///     validFromProperty: "EffectiveDate",
///     validToProperty: "ExpirationDate");
/// 
/// // Open-ended (no end date)
/// new ValidityConfiguration(
///     validFromProperty: "StartDate",
///     validToProperty: null);
/// </code>
/// </example>
public sealed class ValidityConfiguration
{
    /// <summary>
    /// Gets the property representing the start of validity period.
    /// </summary>
    public PropertyMetadata ValidFromProperty { get; }
    
    /// <summary>
    /// Gets the optional property representing the end of validity period.
    /// Null indicates open-ended validity (perpetual until explicitly closed).
    /// </summary>
    public PropertyMetadata? ValidToProperty { get; }
    
    /// <summary>
    /// Gets whether this configuration supports open-ended validity.
    /// </summary>
    public bool IsOpenEnded => ValidToProperty is null;
    
    /// <summary>
    /// Gets the default value for open-ended validity end dates.
    /// </summary>
    public DateTime OpenEndedValue { get; init; } = DateTime.MaxValue;
    
    /// <summary>
    /// Creates a validity configuration with specified property names.
    /// </summary>
    /// <param name="validFromProperty">The property representing validity start.</param>
    /// <param name="validToProperty">The optional property representing validity end.</param>
    public ValidityConfiguration(
        PropertyMetadata validFromProperty,
        PropertyMetadata? validToProperty = null)
    {
        ValidFromProperty = validFromProperty 
            ?? throw new ArgumentNullException(nameof(validFromProperty));
        ValidToProperty = validToProperty;
    }
    
    /// <summary>
    /// Builds a validity predicate expression for the given temporal context.
    /// </summary>
    /// <param name="temporalContext">The point-in-time to filter against.</param>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <returns>An expression that filters entities by validity.</returns>
    public Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(DateTime temporalContext)
    {
        // Implementation generates:
        // e => e.{ValidFromProperty} <= temporalContext 
        //      && (e.{ValidToProperty} > temporalContext || e.{ValidToProperty} == null)
        throw new NotImplementedException();
    }
}
```

### 2.3 Sharding Configuration (Property Agnostic)

```csharp
namespace Dtde.Core.Metadata;

/// <summary>
/// Configuration for entity sharding behavior.
/// Supports multiple strategies with any property as shard key.
/// </summary>
public sealed class ShardingConfiguration
{
    /// <summary>
    /// Gets the sharding strategy type.
    /// </summary>
    public ShardingStrategyType StrategyType { get; }
    
    /// <summary>
    /// Gets the expression that determines the shard key.
    /// Can be a simple property or complex expression.
    /// </summary>
    public LambdaExpression ShardKeyExpression { get; }
    
    /// <summary>
    /// Gets the storage mode for shards.
    /// </summary>
    public ShardStorageMode StorageMode { get; }
    
    /// <summary>
    /// Gets the concrete sharding strategy instance.
    /// </summary>
    public IShardingStrategy Strategy { get; }
    
    /// <summary>
    /// Gets whether migrations are enabled for this sharded entity.
    /// False for manual/pre-created tables.
    /// </summary>
    public bool MigrationsEnabled { get; init; } = true;
}

/// <summary>
/// Supported sharding strategy types.
/// </summary>
public enum ShardingStrategyType
{
    /// <summary>
    /// Shard based on property value equality.
    /// </summary>
    PropertyValue,
    
    /// <summary>
    /// Shard based on date ranges (e.g., by quarter, year).
    /// </summary>
    DateRange,
    
    /// <summary>
    /// Shard based on hash of key value for even distribution.
    /// </summary>
    Hash,
    
    /// <summary>
    /// Shard based on alphabetic ranges (e.g., A-M, N-Z).
    /// </summary>
    Alphabet,
    
    /// <summary>
    /// Shard based on row count (auto-rotate when full).
    /// </summary>
    MaxRows,
    
    /// <summary>
    /// Shard based on multiple keys combined.
    /// </summary>
    Composite,
    
    /// <summary>
    /// Custom sharding logic via expression.
    /// </summary>
    Custom
}

/// <summary>
/// How shards are physically stored.
/// </summary>
public enum ShardStorageMode
{
    /// <summary>
    /// Multiple tables in the same database.
    /// E.g., Orders_EU, Orders_US, Orders_APAC
    /// </summary>
    Tables,
    
    /// <summary>
    /// Separate database per shard.
    /// E.g., EU_Server.Orders, US_Server.Orders
    /// </summary>
    Databases,
    
    /// <summary>
    /// Pre-created tables (e.g., via sqlproj).
    /// No migrations, manual table management.
    /// </summary>
    Manual
}
```

### 2.4 Property Metadata

```csharp
namespace Dtde.Core.Metadata;

/// <summary>
/// Metadata about a single property on an entity.
/// </summary>
public sealed class PropertyMetadata
{
    /// <summary>
    /// Gets the property name.
    /// </summary>
    public string PropertyName { get; }
    
    /// <summary>
    /// Gets the CLR type of the property.
    /// </summary>
    public Type PropertyType { get; }
    
    /// <summary>
    /// Gets the database column name.
    /// </summary>
    public string ColumnName { get; }
    
    /// <summary>
    /// Gets the PropertyInfo reflection metadata.
    /// </summary>
    public PropertyInfo PropertyInfo { get; }
    
    /// <summary>
    /// Gets a compiled getter for fast property access.
    /// </summary>
    public Func<object, object?> GetValue { get; }
    
    /// <summary>
    /// Gets a compiled setter for fast property assignment.
    /// </summary>
    public Action<object, object?> SetValue { get; }
}
```

---

## 3. Relation Metadata

### 3.1 Relation Configuration

```csharp
namespace Dtde.Core.Metadata;

/// <summary>
/// Metadata describing a relationship between two entities.
/// </summary>
public sealed class RelationMetadata
{
    /// <summary>
    /// Gets the parent entity metadata.
    /// </summary>
    public EntityMetadata ParentEntity { get; }
    
    /// <summary>
    /// Gets the child entity metadata.
    /// </summary>
    public EntityMetadata ChildEntity { get; }
    
    /// <summary>
    /// Gets the type of relationship.
    /// </summary>
    public RelationType RelationType { get; }
    
    /// <summary>
    /// Gets the parent key property.
    /// </summary>
    public PropertyMetadata ParentKey { get; }
    
    /// <summary>
    /// Gets the child foreign key property.
    /// </summary>
    public PropertyMetadata ChildForeignKey { get; }
    
    /// <summary>
    /// Gets the temporal containment rule.
    /// </summary>
    public TemporalContainmentRule ContainmentRule { get; }
}

/// <summary>
/// Types of entity relationships.
/// </summary>
public enum RelationType
{
    OneToOne,
    OneToMany,
    ManyToMany
}

/// <summary>
/// Rules for temporal validity containment between parent and child.
/// </summary>
public enum TemporalContainmentRule
{
    /// <summary>
    /// No temporal containment enforced.
    /// </summary>
    None,
    
    /// <summary>
    /// Child validity must be contained within parent validity.
    /// </summary>
    ChildWithinParent,
    
    /// <summary>
    /// Child validity must exactly match parent validity.
    /// </summary>
    ExactMatch
}
```

---

## 4. Shard Metadata

### 4.1 Shard Registry

```csharp
namespace Dtde.Core.Metadata;

/// <summary>
/// Central registry for all shard metadata.
/// Thread-safe and immutable after initialization.
/// </summary>
public interface IShardRegistry
{
    /// <summary>
    /// Gets all registered shards for an entity.
    /// </summary>
    IReadOnlyList<ShardMetadata> GetShards(Type entityType);
    
    /// <summary>
    /// Gets a shard by its unique identifier.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <returns>The shard metadata if found.</returns>
    ShardMetadata? GetShard(string shardId);
    
    /// <summary>
    /// Resolves shards that may contain data for the given criteria.
    /// </summary>
    /// <param name="entity">The entity metadata.</param>
    /// <param name="predicates">Key predicates extracted from query.</param>
    /// <returns>Shards that should be queried.</returns>
    IReadOnlyList<ShardMetadata> ResolveShards(
        EntityMetadata entity,
        IReadOnlyDictionary<string, object?>? predicates = null);
}
```

### 4.2 Shard Metadata

```csharp
namespace Dtde.Core.Metadata;

/// <summary>
/// Metadata for a single shard (table or database).
/// </summary>
public sealed class ShardMetadata
{
    /// <summary>
    /// Gets the unique shard identifier.
    /// </summary>
    /// <example>Orders_2024, Orders_EU, Shard_003</example>
    public string ShardId { get; }
    
    /// <summary>
    /// Gets the display name for logging and diagnostics.
    /// </summary>
    public string Name { get; }
    
    /// <summary>
    /// Gets the storage mode for this shard.
    /// </summary>
    public ShardStorageMode StorageMode { get; }
    
    /// <summary>
    /// Gets the table name (for Table/Manual mode).
    /// </summary>
    public string? TableName { get; }
    
    /// <summary>
    /// Gets the connection string (for Database mode).
    /// </summary>
    public string? ConnectionString { get; }
    
    /// <summary>
    /// Gets the predicate that determines what data belongs to this shard.
    /// </summary>
    public LambdaExpression? ShardPredicate { get; }
    
    /// <summary>
    /// Gets the optional date range this shard covers.
    /// </summary>
    public DateRange? DateRange { get; }
    
    /// <summary>
    /// Gets whether this shard is read-only (e.g., archive).
    /// </summary>
    public bool IsReadOnly { get; init; }
    
    /// <summary>
    /// Gets the priority for query ordering (lower = higher priority).
    /// </summary>
    public int Priority { get; init; } = 100;
}
```

### 4.3 Range Value Objects

```csharp
namespace Dtde.Core.Metadata;

/// <summary>
/// Represents a date range for shard partitioning.
/// </summary>
public readonly record struct DateRange
{
    /// <summary>
    /// Gets the inclusive start date.
    /// </summary>
    public DateTime Start { get; init; }
    
    /// <summary>
    /// Gets the exclusive end date.
    /// </summary>
    public DateTime End { get; init; }
    
    /// <summary>
    /// Determines if a date falls within this range.
    /// </summary>
    /// <param name="date">The date to check.</param>
    /// <returns>True if date is within [Start, End).</returns>
    public bool Contains(DateTime date) => date >= Start && date < End;
    
    /// <summary>
    /// Determines if this range intersects with another range.
    /// </summary>
    /// <param name="other">The other range to check.</param>
    /// <returns>True if ranges overlap.</returns>
    public bool Intersects(DateRange other) => Start < other.End && End > other.Start;
}

/// <summary>
/// Represents a key range for shard partitioning.
/// </summary>
public readonly record struct KeyRange
{
    /// <summary>
    /// Gets the inclusive minimum key value.
    /// </summary>
    public object? MinValue { get; init; }
    
    /// <summary>
    /// Gets the exclusive maximum key value.
    /// </summary>
    public object? MaxValue { get; init; }
    
    /// <summary>
    /// Gets the key type for comparison.
    /// </summary>
    public Type KeyType { get; init; }
}
```

---

## 5. Metadata Registry

### 5.1 Central Registry Interface

```csharp
namespace Dtde.Core.Metadata;

/// <summary>
/// Central registry for all DTDE metadata.
/// Provides access to entity, relation, and shard configurations.
/// </summary>
public interface IMetadataRegistry
{
    /// <summary>
    /// Gets entity metadata by CLR type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <returns>Entity metadata if configured.</returns>
    EntityMetadata? GetEntityMetadata<TEntity>() where TEntity : class;
    
    /// <summary>
    /// Gets entity metadata by CLR type.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <returns>Entity metadata if configured.</returns>
    EntityMetadata? GetEntityMetadata(Type entityType);
    
    /// <summary>
    /// Gets all entity metadata.
    /// </summary>
    IReadOnlyList<EntityMetadata> GetAllEntityMetadata();
    
    /// <summary>
    /// Gets relations for an entity.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <returns>Relations where entity is parent or child.</returns>
    IReadOnlyList<RelationMetadata> GetRelations(Type entityType);
    
    /// <summary>
    /// Gets the shard registry.
    /// </summary>
    IShardRegistry ShardRegistry { get; }
    
    /// <summary>
    /// Validates all metadata for consistency.
    /// </summary>
    /// <returns>Validation result with any errors.</returns>
    MetadataValidationResult Validate();
}
```

### 5.2 Registry Builder

```csharp
namespace Dtde.Core.Metadata;

/// <summary>
/// Builder for constructing the metadata registry during startup.
/// </summary>
public interface IMetadataRegistryBuilder
{
    /// <summary>
    /// Registers entity metadata.
    /// </summary>
    /// <param name="metadata">The entity metadata to register.</param>
    IMetadataRegistryBuilder RegisterEntity(EntityMetadata metadata);
    
    /// <summary>
    /// Registers a relation between entities.
    /// </summary>
    /// <param name="metadata">The relation metadata to register.</param>
    IMetadataRegistryBuilder RegisterRelation(RelationMetadata metadata);
    
    /// <summary>
    /// Registers a shard.
    /// </summary>
    /// <param name="metadata">The shard metadata to register.</param>
    IMetadataRegistryBuilder RegisterShard(ShardMetadata metadata);
    
    /// <summary>
    /// Builds the immutable metadata registry.
    /// </summary>
    /// <returns>The constructed registry.</returns>
    IMetadataRegistry Build();
}
```

---

## 6. Sharding Strategies

### 6.1 Strategy Interface

```csharp
namespace Dtde.Core.Sharding;

/// <summary>
/// Strategy for resolving which shards contain data matching criteria.
/// Follows Open/Closed Principle - extensible without modification.
/// </summary>
public interface IShardingStrategy
{
    /// <summary>
    /// Gets the strategy type.
    /// </summary>
    ShardingStrategyType StrategyType { get; }
    
    /// <summary>
    /// Resolves shards that may contain matching data.
    /// </summary>
    /// <param name="entity">The entity metadata.</param>
    /// <param name="shardRegistry">Available shards.</param>
    /// <param name="predicates">Filter predicates from query.</param>
    /// <param name="temporalContext">Optional temporal filter.</param>
    /// <returns>Shards to query.</returns>
    IReadOnlyList<ShardMetadata> ResolveShards(
        EntityMetadata entity,
        IShardRegistry shardRegistry,
        IReadOnlyDictionary<string, object?> predicates,
        DateTime? temporalContext);
    
    /// <summary>
    /// Determines the target shard for a write operation.
    /// </summary>
    /// <param name="entity">The entity metadata.</param>
    /// <param name="shardRegistry">Available shards.</param>
    /// <param name="entityInstance">The entity being written.</param>
    /// <returns>The target shard for the write.</returns>
    ShardMetadata ResolveWriteShard(
        EntityMetadata entity,
        IShardRegistry shardRegistry,
        object entityInstance);
}
```

### 6.2 Date Range Strategy

```csharp
namespace Dtde.Core.Sharding;

/// <summary>
/// Sharding strategy based on date ranges.
/// Resolves shards by intersecting query date criteria with shard date ranges.
/// </summary>
/// <example>
/// <code>
/// // Shard configuration:
/// // Shard2023Q1: 2023-01-01 to 2023-04-01
/// // Shard2023Q2: 2023-04-01 to 2023-07-01
/// 
/// // Query: ValidAt(2023-03-15)
/// // Result: [Shard2023Q1]
/// 
/// // Query: Date range 2023-03-01 to 2023-05-01
/// // Result: [Shard2023Q1, Shard2023Q2]
/// </code>
/// </example>
public sealed class DateRangeShardingStrategy : IShardingStrategy
{
    public ShardingStrategyType StrategyType => ShardingStrategyType.DateRange;
    
    public IReadOnlyList<ShardMetadata> ResolveShards(
        EntityMetadata entity,
        IShardRegistry shardRegistry,
        IReadOnlyDictionary<string, object?> predicates,
        DateTime? temporalContext)
    {
        var allShards = shardRegistry.GetAllShards();
        
        // If no temporal context and no date predicates, return all shards
        if (temporalContext is null && !HasDatePredicates(predicates, entity))
        {
            return allShards;
        }
        
        // Build query date range from predicates and temporal context
        var queryRange = BuildQueryDateRange(predicates, temporalContext, entity);
        
        // Filter shards by date range intersection
        return allShards
            .Where(s => s.DateRange?.Intersects(queryRange) ?? true)
            .OrderBy(s => s.Priority)
            .ToList();
    }
    
    public ShardMetadata ResolveWriteShard(
        EntityMetadata entity,
        IShardRegistry shardRegistry,
        object entityInstance)
    {
        // Extract validity start date from entity
        var validityConfig = entity.Validity 
            ?? throw new InvalidOperationException(
                $"Entity {entity.ClrType.Name} is not configured for temporal sharding.");
        
        var startDate = (DateTime)validityConfig.ValidFromProperty.GetValue(entityInstance)!;
        
        // Find shard containing this date
        return shardRegistry.GetAllShards()
            .Where(s => !s.IsReadOnly)
            .FirstOrDefault(s => s.DateRange?.Contains(startDate) ?? false)
            ?? throw new ShardNotFoundException(
                $"No writable shard found for date {startDate:yyyy-MM-dd}");
    }
    
    private static bool HasDatePredicates(
        IReadOnlyDictionary<string, object?> predicates,
        EntityMetadata entity)
    {
        if (entity.Validity is null) return false;
        
        var validFromName = entity.Validity.ValidFromProperty.PropertyName;
        var validToName = entity.Validity.ValidToProperty?.PropertyName;
        
        return predicates.ContainsKey(validFromName) 
            || (validToName is not null && predicates.ContainsKey(validToName));
    }
    
    private static DateRange BuildQueryDateRange(
        IReadOnlyDictionary<string, object?> predicates,
        DateTime? temporalContext,
        EntityMetadata entity)
    {
        // Implementation builds date range from predicates and temporal context
        throw new NotImplementedException();
    }
}
```

### 6.3 Hash Sharding Strategy

```csharp
namespace Dtde.Core.Sharding;

/// <summary>
/// Sharding strategy based on consistent hashing of key values.
/// Distributes data evenly across shards.
/// </summary>
public sealed class HashShardingStrategy : IShardingStrategy
{
    private readonly int _hashModulo;
    
    public ShardingStrategyType StrategyType => ShardingStrategyType.Hash;
    
    public HashShardingStrategy(int numberOfShards)
    {
        _hashModulo = numberOfShards;
    }
    
    public IReadOnlyList<ShardMetadata> ResolveShards(
        EntityMetadata entity,
        IShardRegistry shardRegistry,
        IReadOnlyDictionary<string, object?> predicates,
        DateTime? temporalContext)
    {
        var shardKeyProperty = entity.Sharding?.ShardKeyProperties.FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Entity {entity.ClrType.Name} has no shard key configured.");
        
        // If we have an equality predicate on shard key, resolve to single shard
        if (predicates.TryGetValue(shardKeyProperty.PropertyName, out var keyValue) 
            && keyValue is not null)
        {
            var shardIndex = ComputeShardIndex(keyValue);
            var targetShard = shardRegistry.GetAllShards()
                .FirstOrDefault(s => s.ShardId.EndsWith($"_{shardIndex}"));
            
            return targetShard is not null 
                ? new[] { targetShard } 
                : shardRegistry.GetAllShards();
        }
        
        // No key predicate, must query all shards
        return shardRegistry.GetAllShards();
    }
    
    public ShardMetadata ResolveWriteShard(
        EntityMetadata entity,
        IShardRegistry shardRegistry,
        object entityInstance)
    {
        var shardKeyProperty = entity.Sharding?.ShardKeyProperties.FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Entity {entity.ClrType.Name} has no shard key configured.");
        
        var keyValue = shardKeyProperty.GetValue(entityInstance)
            ?? throw new InvalidOperationException(
                $"Shard key {shardKeyProperty.PropertyName} is null.");
        
        var shardIndex = ComputeShardIndex(keyValue);
        
        return shardRegistry.GetAllShards()
            .Where(s => !s.IsReadOnly)
            .FirstOrDefault(s => s.ShardId.EndsWith($"_{shardIndex}"))
            ?? throw new ShardNotFoundException(
                $"No writable shard found for key {keyValue}");
    }
    
    private int ComputeShardIndex(object keyValue)
    {
        return Math.Abs(keyValue.GetHashCode()) % _hashModulo;
    }
}
```

---

## 7. Temporal Context

### 7.1 Context Interface

```csharp
namespace Dtde.Core.Temporal;

/// <summary>
/// Represents the temporal context for queries.
/// Can be set at DbContext level or per-query.
/// </summary>
public interface ITemporalContext
{
    /// <summary>
    /// Gets the current temporal point for filtering.
    /// </summary>
    DateTime? CurrentPoint { get; }
    
    /// <summary>
    /// Gets whether historical data access is enabled.
    /// </summary>
    bool IncludeHistory { get; }
    
    /// <summary>
    /// Gets an optional date range for historical queries.
    /// </summary>
    DateRange? HistoricalRange { get; }
}

/// <summary>
/// Mutable temporal context for DbContext-level configuration.
/// </summary>
public sealed class TemporalContext : ITemporalContext
{
    /// <inheritdoc />
    public DateTime? CurrentPoint { get; private set; }
    
    /// <inheritdoc />
    public bool IncludeHistory { get; private set; }
    
    /// <inheritdoc />
    public DateRange? HistoricalRange { get; private set; }
    
    /// <summary>
    /// Sets the temporal context to a specific point in time.
    /// </summary>
    /// <param name="point">The temporal point.</param>
    public void SetPoint(DateTime point)
    {
        CurrentPoint = point;
        IncludeHistory = false;
        HistoricalRange = null;
    }
    
    /// <summary>
    /// Enables historical data access for a range.
    /// </summary>
    /// <param name="range">The historical range to query.</param>
    public void SetHistoricalRange(DateRange range)
    {
        CurrentPoint = null;
        IncludeHistory = true;
        HistoricalRange = range;
    }
    
    /// <summary>
    /// Enables access to all versions.
    /// </summary>
    public void EnableAllVersions()
    {
        CurrentPoint = null;
        IncludeHistory = true;
        HistoricalRange = null;
    }
    
    /// <summary>
    /// Clears the temporal context (no filtering).
    /// </summary>
    public void Clear()
    {
        CurrentPoint = null;
        IncludeHistory = false;
        HistoricalRange = null;
    }
}
```

---

## 8. Domain Events

### 8.1 Event Definitions

```csharp
namespace Dtde.Core.Events;

/// <summary>
/// Base interface for DTDE domain events.
/// </summary>
public interface IDtdeEvent
{
    /// <summary>
    /// Gets the event timestamp.
    /// </summary>
    DateTime Timestamp { get; }
    
    /// <summary>
    /// Gets the correlation ID for tracing.
    /// </summary>
    string CorrelationId { get; }
}

/// <summary>
/// Raised when shards are resolved for a query.
/// </summary>
public sealed record ShardResolvedEvent(
    DateTime Timestamp,
    string CorrelationId,
    Type EntityType,
    IReadOnlyList<string> ShardIds,
    DateTime? TemporalContext,
    TimeSpan ResolutionDuration) : IDtdeEvent;

/// <summary>
/// Raised when a new entity version is created.
/// </summary>
public sealed record VersionCreatedEvent(
    DateTime Timestamp,
    string CorrelationId,
    Type EntityType,
    object EntityKey,
    DateTime ValidFrom,
    DateTime? ValidTo,
    string TargetShardId) : IDtdeEvent;

/// <summary>
/// Raised when an existing version is invalidated.
/// </summary>
public sealed record VersionInvalidatedEvent(
    DateTime Timestamp,
    string CorrelationId,
    Type EntityType,
    object EntityKey,
    DateTime OriginalValidTo,
    DateTime NewValidTo,
    string ShardId) : IDtdeEvent;

/// <summary>
/// Raised when query execution completes.
/// </summary>
public sealed record QueryExecutedEvent(
    DateTime Timestamp,
    string CorrelationId,
    Type EntityType,
    int ShardCount,
    int TotalRowsReturned,
    TimeSpan TotalDuration,
    IReadOnlyDictionary<string, TimeSpan> ShardDurations) : IDtdeEvent;
```

---

## 9. Exception Types

```csharp
namespace Dtde.Core.Exceptions;

/// <summary>
/// Base exception for DTDE operations.
/// </summary>
public class DtdeException : Exception
{
    public DtdeException(string message) : base(message) { }
    public DtdeException(string message, Exception innerException) 
        : base(message, innerException) { }
}

/// <summary>
/// Thrown when metadata configuration is invalid.
/// </summary>
public sealed class MetadataConfigurationException : DtdeException
{
    public Type? EntityType { get; }
    
    public MetadataConfigurationException(string message, Type? entityType = null) 
        : base(message)
    {
        EntityType = entityType;
    }
}

/// <summary>
/// Thrown when a shard cannot be found for an operation.
/// </summary>
public sealed class ShardNotFoundException : DtdeException
{
    public ShardNotFoundException(string message) : base(message) { }
}

/// <summary>
/// Thrown when a shard operation fails.
/// </summary>
public sealed class ShardOperationException : DtdeException
{
    public string ShardId { get; }
    
    public ShardOperationException(string message, string shardId, Exception? inner = null) 
        : base(message, inner!)
    {
        ShardId = shardId;
    }
}

/// <summary>
/// Thrown when temporal validity constraints are violated.
/// </summary>
public sealed class TemporalValidityException : DtdeException
{
    public Type EntityType { get; }
    public DateTime? RequestedDate { get; }
    
    public TemporalValidityException(
        string message, 
        Type entityType, 
        DateTime? requestedDate = null) 
        : base(message)
    {
        EntityType = entityType;
        RequestedDate = requestedDate;
    }
}
```

---

## 10. Test Specifications

Following the `MethodName_Condition_ExpectedResult` pattern:

### 10.1 Validity Configuration Tests

```csharp
// ValidityConfiguration_WithBothProperties_CreatesCorrectPredicate
// ValidityConfiguration_WithOnlyStartProperty_AllowsOpenEndedValidity
// ValidityConfiguration_BuildPredicate_FiltersCorrectlyForDate
// ValidityConfiguration_BuildPredicate_HandlesNullEndDate
```

### 10.2 Shard Resolution Tests

```csharp
// DateRangeStrategy_WithTemporalContext_ReturnsIntersectingShards
// DateRangeStrategy_WithoutTemporalContext_ReturnsAllShards
// DateRangeStrategy_WriteOperation_ReturnsCorrectShard
// HashStrategy_WithKeyPredicate_ReturnsSingleShard
// HashStrategy_WithoutKeyPredicate_ReturnsAllShards
```

### 10.3 Metadata Registry Tests

```csharp
// MetadataRegistry_GetEntityMetadata_ReturnsConfiguredEntity
// MetadataRegistry_GetEntityMetadata_ReturnsNullForUnconfigured
// MetadataRegistry_Validate_FailsForMissingPrimaryKey
// MetadataRegistry_Validate_FailsForOverlappingShardRanges
```

---

## Next Steps

Continue to [03 - EF Core Integration](03-ef-core-integration.md) for query pipeline and DbContext integration details.
