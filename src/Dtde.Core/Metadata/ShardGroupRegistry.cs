using Dtde.Abstractions.Metadata;

namespace Dtde.Core.Metadata;

/// <summary>
/// Default <see cref="IShardGroupRegistry"/> implementation: builds groups
/// from a flat list of <see cref="IShardMetadata"/> by their
/// <see cref="IShardMetadata.GroupName"/>. The
/// <see cref="IShardGroupRegistry.DefaultGroup"/> is always present, even if
/// no shards were registered without a group.
/// </summary>
public sealed class ShardGroupRegistry : IShardGroupRegistry
{
    private readonly Dictionary<string, IShardGroup> _byName;

    /// <inheritdoc />
    public IShardGroup DefaultGroup { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<IShardGroup> Groups { get; }

    /// <summary>
    /// Builds a registry from a flat list of shards, partitioning them by
    /// <see cref="IShardMetadata.GroupName"/>.
    /// </summary>
    /// <param name="shards">All registered shards.</param>
    public ShardGroupRegistry(IEnumerable<IShardMetadata> shards)
    {
        ArgumentNullException.ThrowIfNull(shards);

        _byName = new Dictionary<string, IShardGroup>(StringComparer.Ordinal);

        foreach (var grouping in shards.GroupBy(s => s.GroupName, StringComparer.Ordinal))
        {
            _byName[grouping.Key] = new ShardGroup(grouping.Key, grouping);
        }

        // The default group is always present so consumers can call
        // DefaultGroup.Shards without first checking for null. An empty default
        // group is fine for applications that route every entity into a named
        // group.
        if (!_byName.TryGetValue(IShardGroupRegistry.DefaultGroupName, out var defaultGroup))
        {
            defaultGroup = new ShardGroup(IShardGroupRegistry.DefaultGroupName, []);
            _byName[IShardGroupRegistry.DefaultGroupName] = defaultGroup;
        }

        DefaultGroup = defaultGroup;
        Groups = _byName.Values.ToArray();
    }

    /// <summary>
    /// Creates an empty registry with only the (empty) default group.
    /// </summary>
    public ShardGroupRegistry() : this([])
    {
    }

    /// <inheritdoc />
    public IShardGroup? FindGroup(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.TryGetValue(name, out var group) ? group : null;
    }
}
