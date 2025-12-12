using System.Collections.Concurrent;

using Dtde.Abstractions.Metadata;

namespace Dtde.Core.Metadata;

/// <summary>
/// Central registry for all DTDE metadata.
/// Thread-safe implementation for entity, relation, and shard configurations.
/// </summary>
public sealed class MetadataRegistry : IMetadataRegistry
{
    private readonly ConcurrentDictionary<Type, IEntityMetadata> _entityMetadata = new();
    private readonly ConcurrentDictionary<Type, List<IRelationMetadata>> _relations = new();

    /// <inheritdoc />
    public IShardRegistry ShardRegistry { get; }

    /// <summary>
    /// Creates a new metadata registry with the specified shard registry.
    /// </summary>
    /// <param name="shardRegistry">The shard registry to use.</param>
    public MetadataRegistry(IShardRegistry shardRegistry)
    {
        ShardRegistry = shardRegistry ?? throw new ArgumentNullException(nameof(shardRegistry));
    }

    /// <summary>
    /// Creates a new metadata registry with an empty shard registry.
    /// </summary>
    public MetadataRegistry() : this(new ShardRegistry())
    {
    }

    /// <inheritdoc />
    public IEntityMetadata? GetEntityMetadata<TEntity>() where TEntity : class
        => GetEntityMetadata(typeof(TEntity));

    /// <inheritdoc />
    public IEntityMetadata? GetEntityMetadata(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return _entityMetadata.GetValueOrDefault(entityType);
    }

    /// <inheritdoc />
    public IReadOnlyList<IEntityMetadata> GetAllEntityMetadata()
        => _entityMetadata.Values.ToList();

    /// <inheritdoc />
    public IReadOnlyList<IRelationMetadata> GetRelations(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return _relations.GetValueOrDefault(entityType) ?? [];
    }

    /// <summary>
    /// Registers entity metadata.
    /// </summary>
    /// <param name="metadata">The entity metadata to register.</param>
    public void RegisterEntity(IEntityMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _entityMetadata[metadata.ClrType] = metadata;
    }

    /// <summary>
    /// Registers a relation between entities.
    /// </summary>
    /// <param name="relation">The relation metadata to register.</param>
    public void RegisterRelation(IRelationMetadata relation)
    {
        ArgumentNullException.ThrowIfNull(relation);

        // Register for both parent and child
        _relations.AddOrUpdate(
            relation.ParentEntity.ClrType,
            [relation],
            (_, existing) => { existing.Add(relation); return existing; });

        _relations.AddOrUpdate(
            relation.ChildEntity.ClrType,
            [relation],
            (_, existing) => { existing.Add(relation); return existing; });
    }

    /// <inheritdoc />
    public MetadataValidationResult Validate()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        foreach (var entity in _entityMetadata.Values)
        {
            // Validate primary key exists
            if (entity.PrimaryKey is null)
            {
                errors.Add($"Entity '{entity.ClrType.Name}' has no primary key configured.");
            }

            // Validate temporal configuration
            if (entity.Validity is not null)
            {
                if (entity.Validity.ValidFromProperty is null)
                {
                    errors.Add($"Entity '{entity.ClrType.Name}' has temporal configuration but no ValidFrom property.");
                }
            }

            // Validate sharding configuration
            if (entity.Sharding is not null)
            {
                if (entity.Sharding.ShardKeyProperties.Count == 0)
                {
                    errors.Add($"Entity '{entity.ClrType.Name}' has sharding configuration but no shard key properties.");
                }
            }
        }

        // Validate relations
        foreach (var relationList in _relations.Values)
        {
            foreach (var relation in relationList)
            {
                // Validate temporal containment rules
                if (relation.ContainmentRule != TemporalContainmentRule.None)
                {
                    if (!relation.ParentEntity.IsTemporal)
                    {
                        errors.Add($"Relation from '{relation.ParentEntity.ClrType.Name}' to '{relation.ChildEntity.ClrType.Name}' has temporal containment but parent is not temporal.");
                    }

                    if (!relation.ChildEntity.IsTemporal)
                    {
                        errors.Add($"Relation from '{relation.ParentEntity.ClrType.Name}' to '{relation.ChildEntity.ClrType.Name}' has temporal containment but child is not temporal.");
                    }
                }
            }
        }

        // Validate shards don't overlap
        var dateShards = ShardRegistry.GetAllShards()
            .Where(s => s.DateRange.HasValue)
            .ToList();

        for (var i = 0; i < dateShards.Count; i++)
        {
            for (var j = i + 1; j < dateShards.Count; j++)
            {
                if (dateShards[i].DateRange!.Value.Intersects(dateShards[j].DateRange!.Value))
                {
                    warnings.Add($"Shards '{dateShards[i].ShardId}' and '{dateShards[j].ShardId}' have overlapping date ranges.");
                }
            }
        }

        if (errors.Count > 0)
        {
            return MetadataValidationResult.Failure(errors, warnings);
        }

        return warnings.Count > 0
            ? MetadataValidationResult.Failure([], warnings)
            : MetadataValidationResult.Success();
    }
}
