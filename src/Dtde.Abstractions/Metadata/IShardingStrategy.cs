namespace Dtde.Abstractions.Metadata;

/// <summary>
/// Strategy for resolving which shards contain data matching query criteria.
/// Follows Open/Closed Principle - extensible without modification.
/// </summary>
public interface IShardingStrategy
{
    /// <summary>
    /// Gets the strategy type identifier.
    /// </summary>
    ShardingStrategyType StrategyType { get; }

    /// <summary>
    /// Resolves shards that may contain data matching the given criteria.
    /// </summary>
    /// <param name="entity">The entity metadata.</param>
    /// <param name="shardRegistry">Available shards.</param>
    /// <param name="predicates">Filter predicates extracted from query (property name to value).</param>
    /// <param name="temporalContext">Optional temporal filter point.</param>
    /// <returns>Shards that should be queried, in priority order.</returns>
    IReadOnlyList<IShardMetadata> ResolveShards(
        IEntityMetadata entity,
        IShardRegistry shardRegistry,
        IReadOnlyDictionary<string, object?> predicates,
        DateTime? temporalContext);

    /// <summary>
    /// Determines the target shard for a write operation.
    /// </summary>
    /// <param name="entity">The entity metadata.</param>
    /// <param name="shardRegistry">Available shards.</param>
    /// <param name="entityInstance">The entity instance being written.</param>
    /// <returns>The target shard for the write.</returns>
    /// <exception cref="Dtde.Abstractions.Exceptions.ShardNotFoundException">Thrown when no suitable shard can be found.</exception>
    IShardMetadata ResolveWriteShard(
        IEntityMetadata entity,
        IShardRegistry shardRegistry,
        object entityInstance);
}
