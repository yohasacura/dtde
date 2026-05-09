namespace Dtde.Abstractions.Metadata;

/// <summary>
/// Helpers for working with shard identifiers across groups.
/// </summary>
public static class ShardIdentityExtensions
{
    /// <summary>
    /// The separator between group name and local shard id in a fully-qualified
    /// shard identifier (<c>"groupName::shardId"</c>).
    /// </summary>
    public const string QualifiedIdSeparator = "::";

    /// <summary>
    /// Returns the shard's fully-qualified identifier — unique across all
    /// groups. For shards in the default group this is just
    /// <see cref="IShardMetadata.ShardId"/> (no prefix); for shards in named
    /// groups it is <c>"groupName::shardId"</c>.
    /// </summary>
    /// <param name="shard">The shard.</param>
    /// <returns>The fully-qualified id.</returns>
    public static string ToQualifiedId(this IShardMetadata shard)
    {
        ArgumentNullException.ThrowIfNull(shard);

        return string.Equals(shard.GroupName, IShardGroupRegistry.DefaultGroupName, StringComparison.Ordinal)
            ? shard.ShardId
            : $"{shard.GroupName}{QualifiedIdSeparator}{shard.ShardId}";
    }
}
