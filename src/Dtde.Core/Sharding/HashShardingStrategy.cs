using Dtde.Abstractions.Exceptions;
using Dtde.Abstractions.Metadata;

namespace Dtde.Core.Sharding;

/// <summary>
/// Sharding strategy based on consistent hashing of key values.
/// Distributes data evenly across shards regardless of key patterns.
/// </summary>
public sealed class HashShardingStrategy : IShardingStrategy
{
    private readonly int _shardCount;

    /// <inheritdoc />
    public ShardingStrategyType StrategyType => ShardingStrategyType.Hash;

    /// <summary>
    /// Creates a hash sharding strategy for the specified number of shards.
    /// </summary>
    /// <param name="shardCount">The total number of shards to distribute across.</param>
    public HashShardingStrategy(int shardCount)
    {
        if (shardCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(shardCount), "Shard count must be positive.");
        }

        _shardCount = shardCount;
    }

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

        // If we have an equality predicate on shard key, resolve to single shard
        if (predicates.TryGetValue(shardKeyProperty.PropertyName, out var keyValue) && keyValue is not null)
        {
            var shardIndex = ComputeShardIndex(keyValue);
            var targetShard = FindShardByIndex(allShards, shardIndex);

            if (targetShard is not null)
            {
                return [targetShard];
            }
        }

        // No key predicate, must query all shards
        return allShards;
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
                $"Entity '{entity.ClrType.Name}' has no shard key configured for hash sharding.");
        }

        var shardKeyProperty = shardKeyProperties[0];

        var keyValue = shardKeyProperty.GetValue(entityInstance)
            ?? throw new ShardNotFoundException(
                $"Shard key property '{shardKeyProperty.PropertyName}' on entity '{entity.ClrType.Name}' is null.");

        var shardIndex = ComputeShardIndex(keyValue);
        var writableShards = shardRegistry.GetWritableShards();
        var targetShard = FindShardByIndex(writableShards, shardIndex);

        return targetShard
            ?? throw new ShardNotFoundException(
                $"No writable shard found for hash index '{shardIndex}' on entity '{entity.ClrType.Name}'.");
    }

    /// <summary>
    /// Computes the shard index for a given key value using consistent hashing.
    /// </summary>
    /// <param name="keyValue">The key value to hash.</param>
    /// <returns>The shard index (0 to ShardCount-1).</returns>
    public int ComputeShardIndex(object keyValue)
    {
        ArgumentNullException.ThrowIfNull(keyValue);

        // Use a consistent hash based on the key's hash code
        var hash = keyValue.GetHashCode();

        // Ensure positive value and map to shard range
        var positiveHash = hash & 0x7FFFFFFF;
        return positiveHash % _shardCount;
    }

    private static IShardMetadata? FindShardByIndex(IReadOnlyList<IShardMetadata> shards, int index)
    {
        if (index < shards.Count)
        {
            return shards[index];
        }

        // Fallback: try to find shard by ID pattern (e.g., "Shard_0", "Shard_1")
        for (var i = 0; i < shards.Count; i++)
        {
            var shard = shards[i];
            if (shard.ShardId.EndsWith($"_{index}", StringComparison.Ordinal) ||
                shard.ShardId.EndsWith($"{index}", StringComparison.Ordinal))
            {
                return shard;
            }
        }

        return null;
    }
}
