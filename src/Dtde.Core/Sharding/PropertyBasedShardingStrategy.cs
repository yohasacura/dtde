using Dtde.Abstractions.Exceptions;
using Dtde.Abstractions.Metadata;

namespace Dtde.Core.Sharding;

/// <summary>
/// Sharding strategy based on simple property value equality.
/// Routes data to shards based on matching shard key values.
/// </summary>
/// <example>
/// <code>
/// // Shard configuration:
/// // Customers_EU: ShardKeyValue = "EU"
/// // Customers_US: ShardKeyValue = "US"
/// // Customers_APAC: ShardKeyValue = "APAC"
/// 
/// // Entity with Region = "US"
/// // Result: Routes to Customers_US shard
/// </code>
/// </example>
public sealed class PropertyBasedShardingStrategy : IShardingStrategy
{
    /// <inheritdoc />
    public ShardingStrategyType StrategyType => ShardingStrategyType.PropertyValue;

    /// <inheritdoc />
    public IReadOnlyList<IShardMetadata> ResolveShards(
        IEntityMetadata entity,
        IShardRegistry shardRegistry,
        IReadOnlyDictionary<string, object?> predicates,
        DateTime? temporalContext)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(shardRegistry);
        ArgumentNullException.ThrowIfNull(predicates);

        var allShards = shardRegistry.GetAllShards();

        if (entity.Sharding is null)
        {
            return allShards;
        }

        var shardKeyProperties = entity.Sharding.ShardKeyProperties;
        if (shardKeyProperties.Count == 0)
        {
            return allShards;
        }

        var shardKeyProperty = shardKeyProperties[0];

        // If we have an equality predicate on shard key, resolve to matching shard(s)
        if (predicates.TryGetValue(shardKeyProperty.PropertyName, out var keyValue) && keyValue is not null)
        {
            var keyString = keyValue.ToString();
            var matchingShards = allShards
                .Where(s => string.Equals(s.ShardKeyValue, keyString, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Priority)
                .ToList();

            if (matchingShards.Count > 0)
            {
                return matchingShards;
            }
        }

        // No specific shard key predicate, return all shards
        return allShards.OrderBy(s => s.Priority).ToList();
    }

    /// <inheritdoc />
    public IShardMetadata ResolveWriteShard(
        IEntityMetadata entity,
        IShardRegistry shardRegistry,
        object entityInstance)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(shardRegistry);
        ArgumentNullException.ThrowIfNull(entityInstance);

        var shardKeyProperties = entity.Sharding?.ShardKeyProperties;
        if (shardKeyProperties is null || shardKeyProperties.Count == 0)
        {
            throw new ShardNotFoundException(
                $"Entity '{entity.ClrType.Name}' has no shard key configured for property-based sharding.");
        }

        var shardKeyProperty = shardKeyProperties[0];

        var keyValue = shardKeyProperty.GetValue(entityInstance)
            ?? throw new ShardNotFoundException(
                $"Shard key property '{shardKeyProperty.PropertyName}' on entity '{entity.ClrType.Name}' is null.");

        var keyString = keyValue.ToString();
        var writableShards = shardRegistry.GetWritableShards();

        // Find shard with matching key value
        var targetShard = writableShards
            .FirstOrDefault(s => string.Equals(s.ShardKeyValue, keyString, StringComparison.OrdinalIgnoreCase));

        // If no exact match, check for a default/catch-all shard
        targetShard ??= writableShards.FirstOrDefault(s => string.IsNullOrEmpty(s.ShardKeyValue));

        return targetShard
            ?? throw new ShardNotFoundException(
                $"No writable shard found for key value '{keyString}' on entity '{entity.ClrType.Name}'.");
    }
}
