using Dtde.Abstractions.Metadata;
using Microsoft.EntityFrameworkCore;

namespace Dtde.EntityFramework.Query;

/// <summary>
/// Factory for creating DbContext instances for specific shards.
/// Supports both database-level and table-level sharding modes.
/// </summary>
public interface IShardContextFactory
{
    /// <summary>
    /// Creates a DbContext for the specified shard.
    /// For database-level sharding, returns a context with the shard's connection string.
    /// For table-level sharding, returns a context configured to use the shard's table name.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A DbContext configured for the specified shard.</returns>
    Task<DbContext> CreateContextAsync(string shardId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a DbContext for the specified shard metadata.
    /// Provides more control for table-level sharding scenarios.
    /// </summary>
    /// <param name="shard">The shard metadata.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A DbContext configured for the specified shard.</returns>
    Task<DbContext> CreateContextAsync(IShardMetadata shard, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the connection string for a specific shard.
    /// For table-level sharding, returns the shared database connection string.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <returns>The connection string.</returns>
    string GetConnectionString(string shardId);

    /// <summary>
    /// Gets the table name for a specific shard (for table-level sharding).
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <returns>The table name, or null if using database-level sharding.</returns>
    string? GetTableName(string shardId);

    /// <summary>
    /// Gets the schema name for a specific shard (for table-level sharding).
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <returns>The schema name, or null if using default schema.</returns>
    string? GetSchemaName(string shardId);
}
