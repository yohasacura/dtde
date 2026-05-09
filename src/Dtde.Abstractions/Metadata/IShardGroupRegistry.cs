namespace Dtde.Abstractions.Metadata;

/// <summary>
/// Top-level registry of <see cref="IShardGroup"/>s. Each group owns its own
/// disjoint set of shards; an entity's
/// <see cref="IShardingConfiguration.ShardGroupName"/> selects which group's
/// shards it routes to.
/// </summary>
public interface IShardGroupRegistry
{
    /// <summary>
    /// The conventional name of the default group. Shards registered without a
    /// group, and entities that don't call <c>UseShardGroup</c>, fall through
    /// to this group.
    /// </summary>
    public const string DefaultGroupName = "__default__";

    /// <summary>
    /// Gets the default group. Always non-<see langword="null"/>; empty if the
    /// application registered no shards without a group.
    /// </summary>
    public IShardGroup DefaultGroup { get; }

    /// <summary>
    /// Looks up a group by name.
    /// </summary>
    /// <param name="name">The group name.</param>
    /// <returns>The group, or <see langword="null"/> if no group with that name is registered.</returns>
    public IShardGroup? FindGroup(string name);

    /// <summary>
    /// Gets every registered group, including the default group.
    /// </summary>
    public IReadOnlyCollection<IShardGroup> Groups { get; }
}
