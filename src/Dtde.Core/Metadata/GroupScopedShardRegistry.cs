using Dtde.Abstractions.Metadata;

namespace Dtde.Core.Metadata;

/// <summary>
/// An <see cref="IShardRegistry"/> view restricted to the shards of a single
/// <see cref="IShardGroup"/>. Used by routing strategies and write resolvers
/// so that an entity's writes never escape its declared shard group.
/// </summary>
public sealed class GroupScopedShardRegistry : IShardRegistry
{
    private readonly IShardGroup _group;

    /// <summary>
    /// Creates a new group-scoped registry.
    /// </summary>
    /// <param name="group">The group whose shards are visible through this registry.</param>
    public GroupScopedShardRegistry(IShardGroup group)
    {
        _group = group ?? throw new ArgumentNullException(nameof(group));
    }

    /// <inheritdoc />
    public IReadOnlyList<IShardMetadata> GetAllShards()
        => _group.Shards;

    /// <inheritdoc />
    public IShardMetadata? GetShard(string shardId)
    {
        ArgumentNullException.ThrowIfNull(shardId);
        return _group.GetShard(shardId);
    }

    /// <inheritdoc />
    public IReadOnlyList<IShardMetadata> GetShardsByTier(ShardTier tier)
        => _group.Shards.Where(s => s.Tier == tier).ToList();

    /// <inheritdoc />
    public IReadOnlyList<IShardMetadata> GetWritableShards()
        => _group.Shards.Where(s => !s.IsReadOnly).ToList();

    /// <inheritdoc />
    public IReadOnlyList<IShardMetadata> GetShardsForDateRange(DateTime startDate, DateTime endDate)
    {
        var queryRange = new DateRange(startDate, endDate);
        return _group.Shards
            .Where(s => s.DateRange is null || s.DateRange.Value.Intersects(queryRange))
            .ToList();
    }
}
