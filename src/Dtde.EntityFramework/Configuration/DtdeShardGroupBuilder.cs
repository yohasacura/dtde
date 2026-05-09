using Dtde.Abstractions.Metadata;
using Dtde.Core.Metadata;

namespace Dtde.EntityFramework.Configuration;

/// <summary>
/// Group-scoped fluent builder. Each call adds a shard to the enclosing
/// <c>AddShardGroup</c>'s group, with the group name baked in automatically —
/// you supply just the local shard id and (for database/mixed mode) the
/// connection string.
/// </summary>
public sealed class DtdeShardGroupBuilder
{
    private readonly string _groupName;
    private readonly List<IShardMetadata> _shards;

    internal DtdeShardGroupBuilder(string groupName, List<IShardMetadata> shards)
    {
        _groupName = groupName;
        _shards = shards;
    }

    /// <summary>
    /// Gets the name of the group being configured.
    /// </summary>
    public string GroupName => _groupName;

    /// <summary>
    /// Adds a table-mode shard to this group.
    /// </summary>
    /// <param name="shardId">The shard's id (unique within this group).</param>
    /// <returns>The group builder for chaining.</returns>
    public DtdeShardGroupBuilder AddShard(string shardId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);

        _shards.Add(new ShardMetadataBuilder()
            .WithId(shardId)
            .WithGroup(_groupName)
            .WithName(shardId)
            .WithShardKeyValue(shardId)
            .WithStorageMode(ShardStorageMode.Tables)
            .Build());
        return this;
    }

    /// <summary>
    /// Adds a database-mode shard to this group: each shard owns its own
    /// database, identified by the supplied connection string.
    /// </summary>
    /// <param name="shardId">The shard's id (unique within this group).</param>
    /// <param name="connectionString">The connection string for this shard's database.</param>
    /// <returns>The group builder for chaining.</returns>
    public DtdeShardGroupBuilder AddShard(string shardId, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _shards.Add(new ShardMetadataBuilder()
            .WithId(shardId)
            .WithGroup(_groupName)
            .WithName(shardId)
            .WithShardKeyValue(shardId)
            .WithConnectionString(connectionString)
            .WithStorageMode(ShardStorageMode.Databases)
            .Build());
        return this;
    }

    /// <summary>
    /// Adds a table-mode shard whose tables live in a specific database (mixed
    /// mode): the shard keeps its per-shard table-name rewrite, but the table
    /// is created inside the supplied connection's database rather than the
    /// parent's default.
    /// </summary>
    /// <param name="shardId">The shard's id (unique within this group).</param>
    /// <param name="connectionString">The connection string of the database hosting this shard's table.</param>
    /// <returns>The group builder for chaining.</returns>
    public DtdeShardGroupBuilder AddTableShardInDatabase(string shardId, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _shards.Add(new ShardMetadataBuilder()
            .WithId(shardId)
            .WithGroup(_groupName)
            .WithName(shardId)
            .WithShardKeyValue(shardId)
            .WithConnectionString(connectionString)
            .WithStorageMode(ShardStorageMode.Tables)
            .Build());
        return this;
    }

    /// <summary>
    /// Adds many table-mode shards in one call.
    /// </summary>
    /// <param name="shardIds">The shard ids.</param>
    /// <returns>The group builder for chaining.</returns>
    public DtdeShardGroupBuilder AddShards(params string[] shardIds)
    {
        ArgumentNullException.ThrowIfNull(shardIds);

        foreach (var id in shardIds)
        {
            AddShard(id);
        }
        return this;
    }

    /// <summary>
    /// Adds a shard configured via the full fluent
    /// <see cref="ShardMetadataBuilder"/>. The group name is forced to this
    /// builder's group; any prior call to <c>WithGroup</c> on the inner
    /// builder is overridden.
    /// </summary>
    /// <param name="configure">Callback that configures the shard.</param>
    /// <returns>The group builder for chaining.</returns>
    public DtdeShardGroupBuilder AddShard(Action<ShardMetadataBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new ShardMetadataBuilder().WithGroup(_groupName);
        configure(builder);
        // Ensure the group can't be overridden by the user's callback.
        builder.WithGroup(_groupName);
        _shards.Add(builder.Build());
        return this;
    }
}
