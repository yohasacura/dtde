using System.Linq.Expressions;

using Dtde.Abstractions.Metadata;
using Dtde.EntityFramework.Configuration;

using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dtde.EntityFramework.Extensions;

/// <summary>
/// Extension methods for configuring sharding and temporal behavior on entities.
/// </summary>
public static class EntityTypeBuilderExtensions
{
    #region Sharding Configuration (Primary Feature)

    /// <summary>
    /// Configures property-based sharding for the entity.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The shard key type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="shardKeySelector">Expression selecting the shard key property.</param>
    /// <returns>A sharding builder for further configuration.</returns>
    /// <example>
    /// <code>
    /// modelBuilder.Entity&lt;Customer&gt;()
    ///     .ShardBy(c =&gt; c.Region)
    ///     .WithStorageMode(ShardStorageMode.Tables);
    /// </code>
    /// </example>
    public static ShardingBuilder<TEntity> ShardBy<TEntity, TKey>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, TKey>> shardKeySelector)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(shardKeySelector);

        var shardKeyProperty = ExtractPropertyName(shardKeySelector);

        builder.Metadata.SetAnnotation(DtdeAnnotationNames.ShardKeyProperty, shardKeyProperty);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.ShardingStrategy, ShardingStrategyType.PropertyValue);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.IsSharded, true);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.StorageMode, ShardStorageMode.Tables);

        return new ShardingBuilder<TEntity>(builder);
    }

    /// <summary>
    /// Configures date-based sharding for the entity.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="dateSelector">Expression selecting the date property.</param>
    /// <param name="interval">The date interval for sharding (Year, Quarter, Month, Day).</param>
    /// <returns>A sharding builder for further configuration.</returns>
    /// <example>
    /// <code>
    /// modelBuilder.Entity&lt;Order&gt;()
    ///     .ShardByDate(o =&gt; o.OrderDate, DateShardInterval.Year)
    ///     .WithStorageMode(ShardStorageMode.Tables);
    /// // Creates: Orders_2023, Orders_2024, etc.
    /// </code>
    /// </example>
    public static ShardingBuilder<TEntity> ShardByDate<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, DateTime>> dateSelector,
        DateShardInterval interval = DateShardInterval.Year)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(dateSelector);

        var shardKeyProperty = ExtractPropertyName(dateSelector);

        builder.Metadata.SetAnnotation(DtdeAnnotationNames.ShardKeyProperty, shardKeyProperty);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.ShardingStrategy, ShardingStrategyType.DateRange);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.DateInterval, interval);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.IsSharded, true);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.StorageMode, ShardStorageMode.Tables);

        return new ShardingBuilder<TEntity>(builder);
    }

    /// <summary>
    /// Configures hash-based sharding for even distribution across shards.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The shard key type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="shardKeySelector">Expression selecting the shard key property.</param>
    /// <param name="shardCount">The total number of shards.</param>
    /// <returns>A sharding builder for further configuration.</returns>
    /// <example>
    /// <code>
    /// modelBuilder.Entity&lt;Product&gt;()
    ///     .ShardByHash(p =&gt; p.Id, shardCount: 8);
    /// // Creates: Products_0, Products_1, ..., Products_7
    /// </code>
    /// </example>
    public static ShardingBuilder<TEntity> ShardByHash<TEntity, TKey>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, TKey>> shardKeySelector,
        int shardCount = 4)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(shardKeySelector);

        if (shardCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(shardCount), "Shard count must be positive.");
        }

        var shardKeyProperty = ExtractPropertyName(shardKeySelector);

        builder.Metadata.SetAnnotation(DtdeAnnotationNames.ShardKeyProperty, shardKeyProperty);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.ShardingStrategy, ShardingStrategyType.Hash);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.ShardCount, shardCount);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.IsSharded, true);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.StorageMode, ShardStorageMode.Tables);

        return new ShardingBuilder<TEntity>(builder);
    }

    /// <summary>
    /// Configures manual sharding with pre-created tables (for sqlproj scenarios).
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="configureManual">Action to configure manual table mappings.</param>
    /// <returns>The entity type builder for chaining.</returns>
    /// <example>
    /// <code>
    /// modelBuilder.Entity&lt;Order&gt;()
    ///     .UseManualSharding(config =&gt;
    ///     {
    ///         config.AddTable("dbo.Orders_2023", o =&gt; o.OrderDate.Year == 2023);
    ///         config.AddTable("dbo.Orders_2024", o =&gt; o.OrderDate.Year == 2024);
    ///         config.MigrationsEnabled = false;
    ///     });
    /// </code>
    /// </example>
    public static EntityTypeBuilder<TEntity> UseManualSharding<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Action<ManualShardingConfiguration<TEntity>> configureManual)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureManual);

        var config = new ManualShardingConfiguration<TEntity>();
        configureManual(config);

        builder.Metadata.SetAnnotation(DtdeAnnotationNames.IsSharded, true);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.StorageMode, ShardStorageMode.Manual);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.ManualShardConfig, config);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.MigrationsEnabled, config.MigrationsEnabled);

        return builder;
    }

    #endregion

    #region Temporal Configuration (Optional Feature)

    /// <summary>
    /// Configures temporal validity properties for the entity.
    /// Property names are fully configurable - no assumptions about naming conventions.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="validFromSelector">Expression selecting the validity start property.</param>
    /// <param name="validToSelector">Optional expression selecting the validity end property.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// // Standard naming
    /// modelBuilder.Entity&lt;Contract&gt;()
    ///     .HasTemporalValidity(c =&gt; c.ValidFrom, c =&gt; c.ValidTo);
    ///
    /// // Domain-specific naming
    /// modelBuilder.Entity&lt;Policy&gt;()
    ///     .HasTemporalValidity(p =&gt; p.EffectiveDate, p =&gt; p.ExpirationDate);
    ///
    /// // Open-ended validity (no end date)
    /// modelBuilder.Entity&lt;Subscription&gt;()
    ///     .HasTemporalValidity(s =&gt; s.StartDate);
    /// </code>
    /// </example>
    public static EntityTypeBuilder<TEntity> HasTemporalValidity<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, DateTime>> validFromSelector,
        Expression<Func<TEntity, DateTime?>>? validToSelector = null)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(validFromSelector);

        var validFromProperty = ExtractPropertyName(validFromSelector);
        var validToProperty = validToSelector is not null
            ? ExtractPropertyName(validToSelector)
            : null;

        builder.Metadata.SetAnnotation(DtdeAnnotationNames.ValidFromProperty, validFromProperty);

        if (validToProperty is not null)
        {
            builder.Metadata.SetAnnotation(DtdeAnnotationNames.ValidToProperty, validToProperty);
        }

        builder.Metadata.SetAnnotation(DtdeAnnotationNames.IsTemporal, true);

        return builder;
    }

    /// <summary>
    /// Configures temporal validity properties for the entity with non-nullable end date.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="validFromSelector">Expression selecting the validity start property.</param>
    /// <param name="validToSelector">Expression selecting the validity end property.</param>
    /// <returns>The builder for chaining.</returns>
    public static EntityTypeBuilder<TEntity> HasTemporalValidity<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, DateTime>> validFromSelector,
        Expression<Func<TEntity, DateTime>> validToSelector)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(validFromSelector);
        ArgumentNullException.ThrowIfNull(validToSelector);

        var validFromProperty = ExtractPropertyName(validFromSelector);
        var validToProperty = ExtractPropertyName(validToSelector);

        builder.Metadata.SetAnnotation(DtdeAnnotationNames.ValidFromProperty, validFromProperty);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.ValidToProperty, validToProperty);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.IsTemporal, true);

        return builder;
    }

    /// <summary>
    /// Configures temporal containment rule for parent-child relationships.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="rule">The temporal containment rule.</param>
    /// <returns>The builder for chaining.</returns>
    public static EntityTypeBuilder<TEntity> HasTemporalContainment<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        TemporalContainmentRule rule)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Metadata.SetAnnotation(DtdeAnnotationNames.TemporalContainment, rule);

        return builder;
    }

    #endregion

    #region Legacy Methods (Backward Compatibility)

    /// <summary>
    /// Configures temporal validity properties for the entity.
    /// </summary>
    /// <remarks>
    /// This method is kept for backward compatibility.
    /// Use <see cref="HasTemporalValidity{TEntity}(EntityTypeBuilder{TEntity}, Expression{Func{TEntity, DateTime}}, Expression{Func{TEntity, DateTime?}}?)"/> instead.
    /// </remarks>
    [Obsolete("Use HasTemporalValidity instead. This method will be removed in a future version.")]
    public static EntityTypeBuilder<TEntity> HasValidity<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, DateTime>> validFromSelector,
        Expression<Func<TEntity, DateTime?>>? validToSelector = null)
        where TEntity : class
    {
        return builder.HasTemporalValidity(validFromSelector, validToSelector);
    }

    /// <summary>
    /// Configures temporal validity properties for the entity with non-nullable end date.
    /// </summary>
    /// <remarks>
    /// This method is kept for backward compatibility.
    /// Use <see cref="HasTemporalValidity{TEntity}(EntityTypeBuilder{TEntity}, Expression{Func{TEntity, DateTime}}, Expression{Func{TEntity, DateTime}})"/> instead.
    /// </remarks>
    [Obsolete("Use HasTemporalValidity instead. This method will be removed in a future version.")]
    public static EntityTypeBuilder<TEntity> HasValidity<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, DateTime>> validFromSelector,
        Expression<Func<TEntity, DateTime>> validToSelector)
        where TEntity : class
    {
        return builder.HasTemporalValidity(validFromSelector, validToSelector);
    }

    /// <summary>
    /// Configures sharding for the entity.
    /// </summary>
    /// <remarks>
    /// This method is kept for backward compatibility.
    /// Use <see cref="ShardBy{TEntity,TKey}"/>, <see cref="ShardByDate{TEntity}"/>, or <see cref="ShardByHash{TEntity,TKey}"/> instead.
    /// </remarks>
    [Obsolete("Use ShardBy, ShardByDate, or ShardByHash instead. This method will be removed in a future version.")]
    public static EntityTypeBuilder<TEntity> UseSharding<TEntity, TKey>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, TKey>> shardKeySelector,
        ShardingStrategyType strategy = ShardingStrategyType.DateRange)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(shardKeySelector);

        var shardKeyProperty = ExtractPropertyName(shardKeySelector);

        builder.Metadata.SetAnnotation(DtdeAnnotationNames.ShardKeyProperty, shardKeyProperty);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.ShardingStrategy, strategy);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.IsSharded, true);

        return builder;
    }

    /// <summary>
    /// Configures composite sharding for the entity.
    /// </summary>
    [Obsolete("Use ShardBy with multiple keys instead. This method will be removed in a future version.")]
    public static EntityTypeBuilder<TEntity> UseCompositeSharding<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        params Expression<Func<TEntity, object>>[] shardKeySelectors)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(shardKeySelectors);

        if (shardKeySelectors.Length < 2)
        {
            throw new ArgumentException(
                "Composite sharding requires at least two key properties.",
                nameof(shardKeySelectors));
        }

        var shardKeyProperties = shardKeySelectors
            .Select(ExtractPropertyName)
            .ToList();

        builder.Metadata.SetAnnotation(DtdeAnnotationNames.ShardKeyProperties, shardKeyProperties);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.ShardingStrategy, ShardingStrategyType.Composite);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.IsSharded, true);

        return builder;
    }

    #endregion

    #region Helpers

    private static string ExtractPropertyName<TEntity, TProperty>(Expression<Func<TEntity, TProperty>> selector)
    {
        if (selector.Body is MemberExpression memberExpression)
        {
            return memberExpression.Member.Name;
        }

        if (selector.Body is UnaryExpression unaryExpression &&
            unaryExpression.Operand is MemberExpression unaryMemberExpression)
        {
            return unaryMemberExpression.Member.Name;
        }

        throw new ArgumentException(
            "Expression must be a property accessor.",
            nameof(selector));
    }

    #endregion
}

/// <summary>
/// Builder for configuring sharding options after initial setup.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public class ShardingBuilder<TEntity> where TEntity : class
{
    private readonly EntityTypeBuilder<TEntity> _builder;

    internal ShardingBuilder(EntityTypeBuilder<TEntity> builder)
    {
        _builder = builder;
    }

    /// <summary>
    /// Sets the storage mode for shards.
    /// </summary>
    /// <param name="mode">The storage mode (Tables, Databases, Manual).</param>
    /// <returns>The sharding builder for chaining.</returns>
    public ShardingBuilder<TEntity> WithStorageMode(ShardStorageMode mode)
    {
        _builder.Metadata.SetAnnotation(DtdeAnnotationNames.StorageMode, mode);
        return this;
    }

    /// <summary>
    /// Sets the table name pattern for table-based sharding.
    /// </summary>
    /// <param name="pattern">The table name pattern (e.g., "{TableName}_{ShardKey}").</param>
    /// <returns>The sharding builder for chaining.</returns>
    public ShardingBuilder<TEntity> WithTablePattern(string pattern)
    {
        _builder.Metadata.SetAnnotation(DtdeAnnotationNames.TableNamePattern, pattern);
        return this;
    }

    /// <summary>
    /// Disables migrations for this sharded entity (tables managed externally).
    /// </summary>
    /// <returns>The sharding builder for chaining.</returns>
    public ShardingBuilder<TEntity> WithoutMigrations()
    {
        _builder.Metadata.SetAnnotation(DtdeAnnotationNames.MigrationsEnabled, false);
        return this;
    }

    /// <summary>
    /// Adds a database shard for database-based sharding.
    /// </summary>
    /// <param name="shardKey">The shard key value.</param>
    /// <param name="connectionString">The connection string for this shard.</param>
    /// <returns>The sharding builder for chaining.</returns>
    public ShardingBuilder<TEntity> AddDatabase(string shardKey, string connectionString)
    {
        var databases = _builder.Metadata.FindAnnotation(DtdeAnnotationNames.ShardDatabases)?.Value
            as Dictionary<string, string> ?? new Dictionary<string, string>();

        databases[shardKey] = connectionString;
        _builder.Metadata.SetAnnotation(DtdeAnnotationNames.ShardDatabases, databases);
        _builder.Metadata.SetAnnotation(DtdeAnnotationNames.StorageMode, ShardStorageMode.Databases);

        return this;
    }

    /// <summary>
    /// Returns the underlying entity type builder for further configuration.
    /// </summary>
    public EntityTypeBuilder<TEntity> Builder => _builder;

    /// <summary>
    /// Implicit conversion to EntityTypeBuilder for seamless chaining.
    /// </summary>
    public static implicit operator EntityTypeBuilder<TEntity>(ShardingBuilder<TEntity> shardingBuilder)
    {
        ArgumentNullException.ThrowIfNull(shardingBuilder);
        return shardingBuilder._builder;
    }

    /// <summary>
    /// Returns the underlying EntityTypeBuilder. Alternate for implicit operator.
    /// </summary>
    public EntityTypeBuilder<TEntity> ToEntityTypeBuilder() => _builder;
}

/// <summary>
/// Configuration for manual table sharding (pre-created tables).
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public class ManualShardingConfiguration<TEntity> where TEntity : class
{
    private readonly List<ManualTableMapping<TEntity>> _tables = new();

    /// <summary>
    /// Gets whether migrations are enabled. Default is false for manual sharding.
    /// </summary>
    public bool MigrationsEnabled { get; set; }

    /// <summary>
    /// Gets the configured table mappings.
    /// </summary>
    public IReadOnlyList<ManualTableMapping<TEntity>> Tables => _tables;

    /// <summary>
    /// Adds a table mapping with a predicate.
    /// </summary>
    /// <param name="tableName">The fully qualified table name (e.g., "dbo.Orders_2024").</param>
    /// <param name="predicate">Expression determining which entities belong to this table.</param>
    public void AddTable(string tableName, Expression<Func<TEntity, bool>> predicate)
    {
        _tables.Add(new ManualTableMapping<TEntity>(tableName, predicate));
    }
}

/// <summary>
/// Represents a manual table mapping with its predicate.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public record ManualTableMapping<TEntity>(string TableName, Expression<Func<TEntity, bool>> Predicate)
    where TEntity : class;
