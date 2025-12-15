namespace Dtde.Abstractions.Metadata;

/// <summary>
/// Represents metadata configuration for a temporal and/or sharded entity.
/// This is the primary aggregate for entity configuration in DTDE.
/// </summary>
public interface IEntityMetadata
{
    /// <summary>
    /// Gets the CLR type of the entity.
    /// </summary>
    Type ClrType { get; }

    /// <summary>
    /// Gets the CLR type of the entity (alias for ClrType).
    /// </summary>
    Type EntityType => ClrType;

    /// <summary>
    /// Gets the database table name for this entity.
    /// </summary>
    string TableName { get; }

    /// <summary>
    /// Gets the database schema name (default: "dbo").
    /// </summary>
    string SchemaName { get; }

    /// <summary>
    /// Gets the primary key property configuration.
    /// May be null if not explicitly configured.
    /// </summary>
    IPropertyMetadata? PrimaryKey { get; }

    /// <summary>
    /// Gets the primary key property configuration (alias for PrimaryKey).
    /// </summary>
    IPropertyMetadata? KeyProperty => PrimaryKey;

    /// <summary>
    /// Gets the optional validity period configuration.
    /// Null if entity is not temporal.
    /// </summary>
    IValidityConfiguration? Validity { get; }

    /// <summary>
    /// Gets the optional validity configuration (alias for Validity).
    /// </summary>
    IValidityConfiguration? ValidityConfiguration => Validity;

    /// <summary>
    /// Gets the optional sharding configuration.
    /// Null if entity is not sharded.
    /// </summary>
    IShardingConfiguration? Sharding { get; }

    /// <summary>
    /// Gets the optional sharding configuration (alias for Sharding).
    /// </summary>
    IShardingConfiguration? ShardingConfiguration => Sharding;

    /// <summary>
    /// Gets whether this entity supports temporal queries.
    /// </summary>
    bool IsTemporal { get; }

    /// <summary>
    /// Gets whether this entity is distributed across shards.
    /// </summary>
    bool IsSharded { get; }
}
