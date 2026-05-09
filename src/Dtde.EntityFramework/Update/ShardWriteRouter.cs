using Dtde.Abstractions.Metadata;
using Dtde.Core.Metadata;
using Dtde.EntityFramework.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Dtde.EntityFramework.Update;

/// <summary>
/// Routes write operations to the appropriate shard based on entity data.
/// Routing is scoped to the entity's
/// <see cref="IShardingConfiguration.ShardGroupName">shard group</see>; an
/// entity's writes never escape its group.
/// </summary>
public sealed class ShardWriteRouter
{
    private readonly IShardRegistry _shardRegistry;
    private readonly IShardGroupRegistry _shardGroupRegistry;
    private readonly IMetadataRegistry _metadataRegistry;
    private readonly ILogger<ShardWriteRouter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShardWriteRouter"/> class.
    /// </summary>
    /// <param name="shardRegistry">The flat shard registry.</param>
    /// <param name="shardGroupRegistry">The shard-group registry.</param>
    /// <param name="metadataRegistry">The metadata registry.</param>
    /// <param name="logger">The logger.</param>
    public ShardWriteRouter(
        IShardRegistry shardRegistry,
        IShardGroupRegistry shardGroupRegistry,
        IMetadataRegistry metadataRegistry,
        ILogger<ShardWriteRouter> logger)
    {
        _shardRegistry = shardRegistry ?? throw new ArgumentNullException(nameof(shardRegistry));
        _shardGroupRegistry = shardGroupRegistry ?? throw new ArgumentNullException(nameof(shardGroupRegistry));
        _metadataRegistry = metadataRegistry ?? throw new ArgumentNullException(nameof(metadataRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the shard registry.
    /// </summary>
    public IShardRegistry ShardRegistry => _shardRegistry;

    /// <summary>
    /// Determines which shard should store the given entity.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity to route.</param>
    /// <returns>The target shard metadata.</returns>
    public IShardMetadata DetermineTargetShard<TEntity>(TEntity entity) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);

        var metadata = _metadataRegistry.GetEntityMetadata<TEntity>();
        if (metadata?.ShardingConfiguration is null)
        {
            LogMessages.EntityNotSharded(_logger, typeof(TEntity).Name);

            return GetDefaultHotShard();
        }

        return ResolveShardByStrategy(entity, metadata);
    }

    /// <summary>
    /// Determines the target shard for a specific date.
    /// </summary>
    /// <param name="date">The date to route for.</param>
    /// <returns>The target shard metadata.</returns>
    public IShardMetadata DetermineShardForDate(DateTime date)
    {
        var candidates = _shardRegistry.GetShardsForDateRange(date, date)
            .Where(s => s.IsActive && s.Tier == ShardTier.Hot)
            .ToList();

        if (candidates.Count == 0)
        {
            LogMessages.NoActiveHotShardForDate(_logger, date);
            return GetDefaultHotShard();
        }

        if (candidates.Count > 1)
        {
            LogMessages.MultipleShardsForDate(_logger, date);
        }

        return candidates.First();
    }

    /// <summary>
    /// Validates that an entity can be written to its target shard.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity to validate.</param>
    /// <param name="targetShard">The target shard.</param>
    /// <returns>True if the entity can be written to the shard.</returns>
    public bool CanWriteToShard<TEntity>(TEntity entity, IShardMetadata targetShard) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(targetShard);

        if (!targetShard.IsActive)
        {
            LogMessages.CannotWriteToInactiveShard(_logger, targetShard.ShardId);
            return false;
        }

        if (targetShard.Tier == ShardTier.Archive)
        {
            LogMessages.CannotWriteToArchiveShard(_logger, targetShard.ShardId);
            return false;
        }

        // Validate date range if applicable
        var metadata = _metadataRegistry.GetEntityMetadata<TEntity>();
        if (metadata?.TemporalConfiguration is not null && targetShard.DateRange is not null)
        {
            var validFrom = (DateTime)metadata.TemporalConfiguration.ValidFromProperty.GetValue(entity)!;
            var dateRange = targetShard.DateRange.Value;

            if (validFrom < dateRange.Start || validFrom >= dateRange.End)
            {
                LogMessages.DateOutsideShardRange(_logger, validFrom, targetShard.ShardId, dateRange.Start, dateRange.End);
                return false;
            }
        }

        return true;
    }

    private IShardMetadata GetDefaultHotShard()
    {
        var hotShards = _shardGroupRegistry.DefaultGroup.Shards
            .Where(s => s.IsActive && s.Tier == ShardTier.Hot)
            .ToList();

        if (hotShards.Count == 0)
        {
            // Fall back to the flat registry as a last resort (e.g. for tests
            // that register a single shard outside any group).
            hotShards = _shardRegistry.GetAllShards()
                .Where(s => s.IsActive && s.Tier == ShardTier.Hot)
                .ToList();
        }

        if (hotShards.Count == 0)
        {
            throw new InvalidOperationException("No active hot shards available for writing.");
        }

        return hotShards.First();
    }

    private IShardMetadata ResolveShardByStrategy<TEntity>(TEntity entity, IEntityMetadata metadata) where TEntity : class
    {
        var shardingConfig = metadata.ShardingConfiguration!;
        var strategy = shardingConfig.Strategy
            ?? throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' has sharding configuration but no strategy.");

        // Constrain the strategy to the entity's group: writes never escape
        // the declared group, so two entities with overlapping local shard ids
        // (e.g. "0" in hash8 vs "0" in hash3) route to the right physical shard.
        var groupName = shardingConfig.ShardGroupName;
        var group = _shardGroupRegistry.FindGroup(groupName)
            ?? throw new InvalidOperationException(
                $"Entity '{typeof(TEntity).Name}' is bound to shard group '{groupName}', but no such " +
                "group is registered. Add it with dtde.AddShardGroup(...) or remove the " +
                "UseShardGroup(...) call on the entity.");

        var groupRegistry = new GroupScopedShardRegistry(group);
        var shard = strategy.ResolveWriteShard(metadata, groupRegistry, entity!);

        LogMessages.RoutingWriteToShard(_logger, typeof(TEntity).Name, shard.ShardId);

        return shard;
    }
}
