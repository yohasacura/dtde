using System.Linq.Expressions;

namespace Dtde.Abstractions.Metadata;

/// <summary>
/// Supported sharding strategy types for distributing data across shards.
/// </summary>
public enum ShardingStrategyType
{
    /// <summary>
    /// Shard based on simple property value equality.
    /// Best for categorical data like Region, TenantId, etc.
    /// </summary>
    PropertyValue,

    /// <summary>
    /// Shard based on date ranges (e.g., by quarter, year, month).
    /// Best for time-series data or data with natural temporal partitioning.
    /// </summary>
    DateRange,

    /// <summary>
    /// Shard based on consistent hash of key value.
    /// Provides even distribution across shards regardless of key patterns.
    /// </summary>
    Hash,

    /// <summary>
    /// Shard based on alphabetic ranges (e.g., A-M, N-Z).
    /// Best for names or string-based keys.
    /// </summary>
    Alphabet,

    /// <summary>
    /// Shard based on row count (auto-rotate when full).
    /// Best for append-only data like logs or events.
    /// </summary>
    MaxRows,

    /// <summary>
    /// Shard based on multiple keys combined.
    /// Enables complex partitioning strategies using multiple dimensions.
    /// </summary>
    Composite,

    /// <summary>
    /// Custom sharding logic provided via expression.
    /// Use when built-in strategies don't meet requirements.
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
    /// E.g., Customers_EU, Customers_US, Customers_APAC
    /// </summary>
    Tables,

    /// <summary>
    /// Separate database per shard.
    /// E.g., EU_Server.Customers, US_Server.Customers
    /// </summary>
    Databases,

    /// <summary>
    /// Pre-created tables (e.g., via sqlproj).
    /// No migrations, manual table management.
    /// </summary>
    Manual
}

/// <summary>
/// Date intervals for date-based sharding.
/// </summary>
public enum DateShardInterval
{
    /// <summary>
    /// Shard by day.
    /// </summary>
    Day,

    /// <summary>
    /// Shard by month.
    /// </summary>
    Month,

    /// <summary>
    /// Shard by quarter (3 months).
    /// </summary>
    Quarter,

    /// <summary>
    /// Shard by year.
    /// </summary>
    Year
}

/// <summary>
/// Configuration for entity sharding behavior.
/// Supports multiple sharding strategies with configurable shard key properties.
/// </summary>
public interface IShardingConfiguration
{
    /// <summary>
    /// Gets the type of sharding strategy being used.
    /// </summary>
    ShardingStrategyType StrategyType { get; }

    /// <summary>
    /// Gets the storage mode for shards (Tables, Databases, or Manual).
    /// </summary>
    ShardStorageMode StorageMode { get; }

    /// <summary>
    /// Gets the expression that determines the shard key.
    /// Can be a simple property or complex expression.
    /// </summary>
    LambdaExpression? ShardKeyExpression { get; }

    /// <summary>
    /// Gets the properties used as shard key(s).
    /// For composite strategies, multiple properties may be included.
    /// </summary>
    IReadOnlyList<IPropertyMetadata> ShardKeyProperties { get; }

    /// <summary>
    /// Gets the concrete sharding strategy instance for shard resolution.
    /// </summary>
    IShardingStrategy Strategy { get; }

    /// <summary>
    /// Gets whether migrations are enabled for this sharded entity.
    /// False for manual/pre-created tables.
    /// </summary>
    bool MigrationsEnabled { get; }

    /// <summary>
    /// Gets the table name pattern for table-based sharding.
    /// E.g., "{TableName}_{ShardKey}" produces "Customers_EU", "Customers_US".
    /// </summary>
    string? TableNamePattern { get; }

    /// <summary>
    /// Gets the date interval for date-based sharding.
    /// Only applicable when StrategyType is DateRange.
    /// </summary>
    DateShardInterval? DateInterval { get; }
}
