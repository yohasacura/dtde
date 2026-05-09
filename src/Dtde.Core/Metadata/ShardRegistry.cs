using System.Collections.Concurrent;

using Dtde.Abstractions.Metadata;

namespace Dtde.Core.Metadata;

/// <summary>
/// Implementation of the shard registry.
/// Thread-safe and optimized for read operations.
/// </summary>
/// <remarks>
/// Lookups by shard id (<see cref="GetShard(string)"/>) match shards in the
/// default group by their local id (<c>"EU"</c> resolves to the EU shard in
/// the default group). To look up a shard inside a named group, prefer the
/// fully-qualified id <c>"groupName::shardId"</c> — both forms are accepted.
/// For group-scoped reasoning, use <see cref="IShardGroupRegistry"/> instead.
/// </remarks>
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
        _shardsById = new ConcurrentDictionary<string, IShardMetadata>(StringComparer.Ordinal);
        foreach (var shard in shardList)
        {
            // Default-group shards keep their plain local id as the lookup key
            // (so existing code that looks up by "EU" still works). Named-group
            // shards are keyed by the fully-qualified "group::id" form so two
            // shards with the same local id in different groups don't collide.
            var key = string.Equals(shard.GroupName, IShardGroupRegistry.DefaultGroupName, StringComparison.Ordinal)
                ? shard.ShardId
                : $"{shard.GroupName}::{shard.ShardId}";
            _shardsById[key] = shard;
        }
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

        var key = string.Equals(shard.GroupName, IShardGroupRegistry.DefaultGroupName, StringComparison.Ordinal)
            ? shard.ShardId
            : $"{shard.GroupName}::{shard.ShardId}";

        if (_shardsById.TryAdd(key, shard))
        {
            _orderedShards.Add(shard);
            _orderedShards.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }
    }
}
