using System.Collections.Concurrent;

using Dtde.Abstractions.Metadata;

namespace Dtde.Core.Metadata;

/// <summary>
/// Implementation of the shard registry.
/// Thread-safe and optimized for read operations.
/// </summary>
public sealed class ShardRegistry : IShardRegistry
{
    private readonly ConcurrentDictionary<string, IShardMetadata> _shardsById;
    private readonly List<IShardMetadata> _orderedShards;

    /// <summary>
    /// Creates a new shard registry with the specified shards.
    /// </summary>
    /// <param name="shards">The shards to register.</param>
    public ShardRegistry(IEnumerable<IShardMetadata> shards)
    {
        ArgumentNullException.ThrowIfNull(shards);

        var shardList = shards.ToList();
        _shardsById = new ConcurrentDictionary<string, IShardMetadata>(
            shardList.ToDictionary(s => s.ShardId, s => s));
        _orderedShards = [.. shardList.OrderBy(s => s.Priority)];
    }

    /// <summary>
    /// Creates an empty shard registry.
    /// </summary>
    public ShardRegistry() : this([])
    {
    }

    /// <inheritdoc />
    public IReadOnlyList<IShardMetadata> GetAllShards() => _orderedShards;

    /// <inheritdoc />
    public IShardMetadata? GetShard(string shardId)
    {
        ArgumentNullException.ThrowIfNull(shardId);
        return _shardsById.GetValueOrDefault(shardId);
    }

    /// <inheritdoc />
    public IReadOnlyList<IShardMetadata> GetShardsByTier(ShardTier tier)
        => _orderedShards.Where(s => s.Tier == tier).ToList();

    /// <inheritdoc />
    public IReadOnlyList<IShardMetadata> GetWritableShards()
        => _orderedShards.Where(s => !s.IsReadOnly).ToList();

    /// <inheritdoc />
    public IReadOnlyList<IShardMetadata> GetShardsForDateRange(DateTime startDate, DateTime endDate)
    {
        var queryRange = new DateRange(startDate, endDate);

        return _orderedShards
            .Where(s => s.DateRange is null || s.DateRange.Value.Intersects(queryRange))
            .ToList();
    }

    /// <summary>
    /// Adds a shard to the registry.
    /// </summary>
    /// <param name="shard">The shard to add.</param>
    public void AddShard(IShardMetadata shard)
    {
        ArgumentNullException.ThrowIfNull(shard);

        if (_shardsById.TryAdd(shard.ShardId, shard))
        {
            _orderedShards.Add(shard);
            _orderedShards.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }
    }
}
