using Dtde.Abstractions.Metadata;
using Dtde.EntityFramework.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Dtde.EntityFramework.Update;

/// <summary>
/// Routes write operations to the appropriate shard based on entity data.
/// </summary>
public sealed class ShardWriteRouter
{
    private readonly IShardRegistry _shardRegistry;
    private readonly IMetadataRegistry _metadataRegistry;
    private readonly ILogger<ShardWriteRouter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShardWriteRouter"/> class.
    /// </summary>
    /// <param name="shardRegistry">The shard registry.</param>
    /// <param name="metadataRegistry">The metadata registry.</param>
    /// <param name="logger">The logger.</param>
    public ShardWriteRouter(
        IShardRegistry shardRegistry,
        IMetadataRegistry metadataRegistry,
        ILogger<ShardWriteRouter> logger)
    {
        _shardRegistry = shardRegistry ?? throw new ArgumentNullException(nameof(shardRegistry));
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
        if (metadata?.ValidityConfiguration is not null && targetShard.DateRange is not null)
        {
            var validFrom = (DateTime)metadata.ValidityConfiguration.ValidFromProperty.GetValue(entity)!;
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
        var hotShards = _shardRegistry.GetAllShards()
            .Where(s => s.IsActive && s.Tier == ShardTier.Hot)
            .ToList();

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

        // Use the strategy to resolve the write shard
        var shard = strategy.ResolveWriteShard(metadata, _shardRegistry, entity!);

        LogMessages.RoutingWriteToShard(_logger, typeof(TEntity).Name, shard.ShardId);

        return shard;
    }
}
