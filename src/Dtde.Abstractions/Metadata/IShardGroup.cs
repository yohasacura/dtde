namespace Dtde.Abstractions.Metadata;

/// <summary>
/// A named set of shards. Entities are bound to a single group; queries and
/// provisioning fan out across the shards of that group only — never across
/// groups.
/// </summary>
/// <remarks>
/// <para>
/// Shard groups solve the entity-to-shard mapping problem: when one entity
/// shards into 8 hash buckets and another into 3 yearly buckets, both can't
/// share a single global shard pool. Each entity declares which group's
/// shards it lives on; shard ids are unique <em>within</em> a group, so
/// <c>"0"</c> in a <c>hash8</c> group is a different physical shard from
/// <c>"0"</c> in a <c>hash3</c> group.
/// </para>
/// <para>
/// When the application registers shards without a group
/// (<c>dtde.AddShards("EU", "US")</c>), they fall into the conventional
/// default group (<see cref="IShardGroupRegistry.DefaultGroupName"/>), and
/// entities that don't call <c>UseShardGroup</c> bind to it implicitly. The
/// simple "one shard topology for everything" case therefore stays
/// configuration-free.
/// </para>
/// </remarks>
public interface IShardGroup
{
    /// <summary>
    /// Gets the group's name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the shards in this group, in priority order. Each shard's
    /// <see cref="IShardMetadata.ShardId"/> is unique within this group.
    /// </summary>
    public IReadOnlyList<IShardMetadata> Shards { get; }

    /// <summary>
    /// Looks up a shard in this group by its local id.
    /// </summary>
    /// <param name="shardId">The shard's id within this group.</param>
    /// <returns>The shard, or <see langword="null"/> if no shard with that id exists in this group.</returns>
    public IShardMetadata? GetShard(string shardId);
}
