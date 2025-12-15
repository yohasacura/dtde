using System.Linq.Expressions;
using Dtde.Abstractions.Metadata;

namespace Dtde.Core.Metadata;

/// <summary>
/// Configuration for entity sharding behavior.
/// </summary>
public sealed class ShardingConfiguration : IShardingConfiguration
{
    /// <inheritdoc />
    public ShardingStrategyType StrategyType { get; }

    /// <inheritdoc />
    public ShardStorageMode StorageMode { get; }

    /// <inheritdoc />
    public LambdaExpression? ShardKeyExpression { get; }

    /// <inheritdoc />
    public IReadOnlyList<IPropertyMetadata> ShardKeyProperties { get; }

    /// <inheritdoc />
    public IShardingStrategy Strategy { get; }

    /// <inheritdoc />
    public bool MigrationsEnabled { get; init; } = true;

    /// <inheritdoc />
    public string? TableNamePattern { get; init; }

    /// <inheritdoc />
    public DateShardInterval? DateInterval { get; init; }

    /// <summary>
    /// Creates a sharding configuration with the specified settings.
    /// </summary>
    /// <param name="strategyType">The type of sharding strategy.</param>
    /// <param name="storageMode">The storage mode for shards.</param>
    /// <param name="shardKeyExpression">The expression for the shard key.</param>
    /// <param name="shardKeyProperties">The properties used as shard keys.</param>
    /// <param name="strategy">The sharding strategy implementation.</param>
    public ShardingConfiguration(
        ShardingStrategyType strategyType,
        ShardStorageMode storageMode,
        LambdaExpression? shardKeyExpression,
        IReadOnlyList<IPropertyMetadata> shardKeyProperties,
        IShardingStrategy strategy)
    {
        StrategyType = strategyType;
        StorageMode = storageMode;
        ShardKeyExpression = shardKeyExpression;
        ShardKeyProperties = shardKeyProperties ?? throw new ArgumentNullException(nameof(shardKeyProperties));
        Strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
    }

    /// <summary>
    /// Creates a sharding configuration for property-based sharding.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The shard key type.</typeparam>
    /// <param name="shardKeySelector">Expression selecting the shard key property.</param>
    /// <param name="storageMode">The storage mode for shards.</param>
    /// <param name="strategy">The sharding strategy implementation.</param>
    /// <returns>A new sharding configuration.</returns>
    public static ShardingConfiguration Create<TEntity, TKey>(
        Expression<Func<TEntity, TKey>> shardKeySelector,
        ShardStorageMode storageMode,
        IShardingStrategy strategy)
        where TEntity : class
    {
        var property = PropertyMetadata.FromExpression(shardKeySelector);
        return new ShardingConfiguration(
            ShardingStrategyType.PropertyValue,
            storageMode,
            shardKeySelector,
            new[] { property },
            strategy);
    }

    /// <summary>
    /// Creates a sharding configuration for date-based sharding.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="dateSelector">Expression selecting the date property.</param>
    /// <param name="interval">The date interval for sharding.</param>
    /// <param name="storageMode">The storage mode for shards.</param>
    /// <param name="strategy">The sharding strategy implementation.</param>
    /// <returns>A new sharding configuration.</returns>
    public static ShardingConfiguration CreateDateBased<TEntity>(
        Expression<Func<TEntity, DateTime>> dateSelector,
        DateShardInterval interval,
        ShardStorageMode storageMode,
        IShardingStrategy strategy)
        where TEntity : class
    {
        var property = PropertyMetadata.FromExpression(dateSelector);
        return new ShardingConfiguration(
            ShardingStrategyType.DateRange,
            storageMode,
            dateSelector,
            new[] { property },
            strategy)
        {
            DateInterval = interval
        };
    }
}
