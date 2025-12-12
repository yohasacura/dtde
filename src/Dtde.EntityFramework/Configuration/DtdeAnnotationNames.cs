namespace Dtde.EntityFramework.Configuration;

/// <summary>
/// Annotation names used by DTDE in EF Core model.
/// </summary>
public static class DtdeAnnotationNames
{
    /// <summary>
    /// Annotation prefix for all DTDE annotations.
    /// </summary>
    public const string Prefix = "Dtde:";

    #region Temporal Annotations

    /// <summary>
    /// Annotation for the ValidFrom property name.
    /// </summary>
    public const string ValidFromProperty = Prefix + "ValidFromProperty";

    /// <summary>
    /// Annotation for the ValidTo property name.
    /// </summary>
    public const string ValidToProperty = Prefix + "ValidToProperty";

    /// <summary>
    /// Annotation indicating the entity is temporal.
    /// </summary>
    public const string IsTemporal = Prefix + "IsTemporal";

    /// <summary>
    /// Annotation for temporal containment rule.
    /// </summary>
    public const string TemporalContainment = Prefix + "TemporalContainment";

    #endregion

    #region Sharding Annotations

    /// <summary>
    /// Annotation for the shard key property name.
    /// </summary>
    public const string ShardKeyProperty = Prefix + "ShardKeyProperty";

    /// <summary>
    /// Annotation for composite shard key property names.
    /// </summary>
    public const string ShardKeyProperties = Prefix + "ShardKeyProperties";

    /// <summary>
    /// Annotation for the sharding strategy type.
    /// </summary>
    public const string ShardingStrategy = Prefix + "ShardingStrategy";

    /// <summary>
    /// Annotation indicating the entity is sharded.
    /// </summary>
    public const string IsSharded = Prefix + "IsSharded";

    /// <summary>
    /// Annotation for the shard storage mode (Tables, Databases, Manual).
    /// </summary>
    public const string StorageMode = Prefix + "StorageMode";

    /// <summary>
    /// Annotation for the date interval in date-based sharding.
    /// </summary>
    public const string DateInterval = Prefix + "DateInterval";

    /// <summary>
    /// Annotation for the shard count in hash-based sharding.
    /// </summary>
    public const string ShardCount = Prefix + "ShardCount";

    /// <summary>
    /// Annotation for the table name pattern.
    /// </summary>
    public const string TableNamePattern = Prefix + "TableNamePattern";

    /// <summary>
    /// Annotation indicating if migrations are enabled.
    /// </summary>
    public const string MigrationsEnabled = Prefix + "MigrationsEnabled";

    /// <summary>
    /// Annotation for shard database connection strings.
    /// </summary>
    public const string ShardDatabases = Prefix + "ShardDatabases";

    /// <summary>
    /// Annotation for manual shard configuration.
    /// </summary>
    public const string ManualShardConfig = Prefix + "ManualShardConfig";

    #endregion
}
