namespace Dtde.Abstractions.Metadata;

/// <summary>
/// Central registry for all shard metadata.
/// Provides access to shard configurations and resolution.
/// </summary>
public interface IShardRegistry
{
    /// <summary>
    /// Gets all registered shards.
    /// </summary>
    /// <returns>All shard metadata in priority order.</returns>
    IReadOnlyList<IShardMetadata> GetAllShards();

    /// <summary>
    /// Gets a shard by its unique identifier.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <returns>The shard metadata if found, null otherwise.</returns>
    IShardMetadata? GetShard(string shardId);

    /// <summary>
    /// Gets shards filtered by tier.
    /// </summary>
    /// <param name="tier">The tier to filter by.</param>
    /// <returns>Shards matching the specified tier.</returns>
    IReadOnlyList<IShardMetadata> GetShardsByTier(ShardTier tier);

    /// <summary>
    /// Gets writable shards (excluding read-only shards).
    /// </summary>
    /// <returns>All writable shards.</returns>
    IReadOnlyList<IShardMetadata> GetWritableShards();

    /// <summary>
    /// Gets shards that cover the specified date range.
    /// </summary>
    /// <param name="startDate">The start date of the range.</param>
    /// <param name="endDate">The end date of the range.</param>
    /// <returns>Shards that contain data within the date range.</returns>
    IReadOnlyList<IShardMetadata> GetShardsForDateRange(DateTime startDate, DateTime endDate);
}
