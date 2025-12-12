using Dtde.Abstractions.Exceptions;
using Dtde.Abstractions.Metadata;

namespace Dtde.Core.Sharding;

/// <summary>
/// Sharding strategy based on date ranges.
/// Resolves shards by intersecting query date criteria with shard date ranges.
/// </summary>
/// <example>
/// <code>
/// // Shard configuration:
/// // Shard2023Q1: 2023-01-01 to 2023-04-01
/// // Shard2023Q2: 2023-04-01 to 2023-07-01
///
/// // Query: ValidAt(2023-03-15)
/// // Result: [Shard2023Q1]
///
/// // Query: Date range 2023-03-01 to 2023-05-01
/// // Result: [Shard2023Q1, Shard2023Q2]
/// </code>
/// </example>
public sealed class DateRangeShardingStrategy : IShardingStrategy
{
    /// <inheritdoc />
    public ShardingStrategyType StrategyType => ShardingStrategyType.DateRange;

    /// <inheritdoc />
    public IReadOnlyList<IShardMetadata> ResolveShards(
        IEntityMetadata entity,
        IShardRegistry shardRegistry,
        IReadOnlyDictionary<string, object?> predicates,
        DateTime? temporalContext)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(shardRegistry);
        ArgumentNullException.ThrowIfNull(predicates);

        var allShards = shardRegistry.GetAllShards();

        // If no temporal context and no date predicates, return all shards
        if (temporalContext is null && !HasDatePredicates(predicates, entity))
        {
            return allShards;
        }

        // Build query date range from predicates and temporal context
        var queryRange = BuildQueryDateRange(predicates, temporalContext, entity);

        if (queryRange is null)
        {
            return allShards;
        }

        // Filter shards by date range intersection
        return allShards
            .Where(s => s.DateRange is null || s.DateRange.Value.Intersects(queryRange.Value))
            .OrderBy(s => s.Priority)
            .ToList();
    }

    /// <inheritdoc />
    public IShardMetadata ResolveWriteShard(
        IEntityMetadata entity,
        IShardRegistry shardRegistry,
        object entityInstance)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(shardRegistry);
        ArgumentNullException.ThrowIfNull(entityInstance);

        // Get the shard key property value
        var shardKeyProperties = entity.Sharding?.ShardKeyProperties;
        IPropertyMetadata shardKeyProperty;
        if (shardKeyProperties is not null && shardKeyProperties.Count > 0)
        {
            shardKeyProperty = shardKeyProperties[0];
        }
        else if (entity.Validity?.ValidFromProperty is not null)
        {
            shardKeyProperty = entity.Validity.ValidFromProperty;
        }
        else
        {
            throw new ShardNotFoundException(
                $"Entity '{entity.ClrType.Name}' has no shard key or validity property configured for date range sharding.");
        }

        var keyValue = shardKeyProperty.GetValue(entityInstance);

        if (keyValue is not DateTime dateValue)
        {
            throw new ShardNotFoundException(
                $"Shard key property '{shardKeyProperty.PropertyName}' on entity '{entity.ClrType.Name}' is not a DateTime.");
        }

        // Find shard containing this date
        var targetShard = shardRegistry.GetWritableShards()
            .FirstOrDefault(s => s.DateRange?.Contains(dateValue) ?? false);

        if (targetShard is null)
        {
            // Try to find a shard without date range (catch-all shard)
            targetShard = shardRegistry.GetWritableShards()
                .FirstOrDefault(s => s.DateRange is null);
        }

        return targetShard
            ?? throw new ShardNotFoundException(
                $"No writable shard found for date '{dateValue:yyyy-MM-dd}' on entity '{entity.ClrType.Name}'.");
    }

    private static bool HasDatePredicates(
        IReadOnlyDictionary<string, object?> predicates,
        IEntityMetadata entity)
    {
        if (entity.Validity is null)
        {
            return false;
        }

        var validFromName = entity.Validity.ValidFromProperty.PropertyName;
        var validToName = entity.Validity.ValidToProperty?.PropertyName;

        return predicates.ContainsKey(validFromName)
            || (validToName is not null && predicates.ContainsKey(validToName));
    }

    private static DateRange? BuildQueryDateRange(
        IReadOnlyDictionary<string, object?> predicates,
        DateTime? temporalContext,
        IEntityMetadata entity)
    {
        // If we have a specific temporal context, create a point-in-time range
        if (temporalContext.HasValue)
        {
            // For point-in-time queries, we need shards that contain this date
            var point = temporalContext.Value;
            return new DateRange(point, point.AddTicks(1));
        }

        // Try to extract date range from predicates
        DateTime? start = null;
        DateTime? end = null;

        if (entity.Validity is not null)
        {
            var validFromName = entity.Validity.ValidFromProperty.PropertyName;
            var validToName = entity.Validity.ValidToProperty?.PropertyName;

            if (predicates.TryGetValue(validFromName, out var fromValue) && fromValue is DateTime fromDate)
            {
                start = fromDate;
            }

            if (validToName is not null &&
                predicates.TryGetValue(validToName, out var toValue) && toValue is DateTime toDate)
            {
                end = toDate;
            }
        }

        // Also check shard key properties
        if (entity.Sharding is not null)
        {
            var dateKeyValues = entity.Sharding.ShardKeyProperties
                .Select(keyProp => predicates.TryGetValue(keyProp.PropertyName, out var value) && value is DateTime dt ? dt : (DateTime?)null)
                .Where(d => d.HasValue)
                .Select(d => d!.Value);

            foreach (var dateValue in dateKeyValues)
            {
                start ??= dateValue;
                end ??= dateValue.AddTicks(1);
            }
        }

        if (start.HasValue || end.HasValue)
        {
            return new DateRange(
                start ?? DateTime.MinValue,
                end ?? DateTime.MaxValue);
        }

        return null;
    }
}
