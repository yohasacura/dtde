using Dtde.Abstractions.Temporal;

namespace Dtde.Abstractions.Metadata;

/// <summary>
/// Aggregate describing how a CLR entity type is mapped, sharded, and (optionally)
/// version-tracked by DTDE. This is the primary read-side metadata model for
/// downstream components such as the query rewriter and write router.
/// </summary>
public interface IEntityMetadata
{
    /// <summary>
    /// Gets the CLR <see cref="Type"/> of the entity.
    /// </summary>
    public Type ClrType { get; }

    /// <summary>
    /// Gets the database table name for this entity.
    /// </summary>
    public string TableName { get; }

    /// <summary>
    /// Gets the database schema name (default: <c>"dbo"</c>).
    /// </summary>
    public string SchemaName { get; }

    /// <summary>
    /// Gets the primary-key property configuration, or <see langword="null"/> when not
    /// explicitly modelled (DTDE will fall back to EF Core's primary-key inference).
    /// </summary>
    public IPropertyMetadata? PrimaryKey { get; }

    /// <summary>
    /// Gets the temporal-versioning configuration, or <see langword="null"/> when the
    /// entity is not temporal.
    /// </summary>
    public ITemporalConfiguration? TemporalConfiguration { get; }

    /// <summary>
    /// Gets the sharding configuration, or <see langword="null"/> when the entity is
    /// not distributed.
    /// </summary>
    public IShardingConfiguration? ShardingConfiguration { get; }

    /// <summary>
    /// Gets a value indicating whether this entity supports temporal queries.
    /// Equivalent to <c>TemporalConfiguration is not null</c>.
    /// </summary>
    public bool IsTemporal { get; }

    /// <summary>
    /// Gets a value indicating whether this entity is distributed across shards.
    /// Equivalent to <c>ShardingConfiguration is not null</c>.
    /// </summary>
    public bool IsSharded { get; }
}
