using Dtde.Abstractions.Metadata;

namespace Dtde.Core.Metadata;

/// <summary>
/// Default <see cref="IShardGroup"/> implementation: a name plus an
/// immutable, priority-ordered list of shards.
/// </summary>
public sealed class ShardGroup : IShardGroup
{
    private readonly Dictionary<string, IShardMetadata> _shardsById;

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IReadOnlyList<IShardMetadata> Shards { get; }

    /// <summary>
    /// Creates a new shard group.
    /// </summary>
    /// <param name="name">The group's name.</param>
    /// <param name="shards">The shards belonging to this group.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is null or whitespace, or when two
    /// shards in <paramref name="shards"/> share the same id.
    /// </exception>
    public ShardGroup(string name, IEnumerable<IShardMetadata> shards)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(shards);

        Name = name;

        var ordered = shards.OrderBy(s => s.Priority).ToList();
        Shards = ordered;

        _shardsById = new Dictionary<string, IShardMetadata>(StringComparer.Ordinal);
        foreach (var shard in ordered)
        {
            if (!_shardsById.TryAdd(shard.ShardId, shard))
            {
                throw new ArgumentException(
                    $"Group '{name}' has more than one shard with id '{shard.ShardId}'. " +
                    "Shard ids must be unique within a group.",
                    nameof(shards));
            }
        }
    }

    /// <inheritdoc />
    public IShardMetadata? GetShard(string shardId)
    {
        ArgumentNullException.ThrowIfNull(shardId);
        return _shardsById.TryGetValue(shardId, out var shard) ? shard : null;
    }
}
