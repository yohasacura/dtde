using System.Linq.Expressions;

using Dtde.Abstractions.Metadata;
using Dtde.EntityFramework.Configuration;

using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dtde.EntityFramework.Extensions;

/// <summary>
/// EF Core <see cref="EntityTypeBuilder{TEntity}"/> extensions that configure DTDE's
/// sharding and temporal behaviour declaratively from <c>OnModelCreating</c>.
/// </summary>
public static class EntityTypeBuilderExtensions
{
    // ----------------------------------------------------------------------
    //  Sharding
    // ----------------------------------------------------------------------

    /// <summary>
    /// Configures property-value sharding: rows are routed by a discriminator property such as a
    /// region code or tenant identifier.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The shard-key property type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="shardKeySelector">Expression selecting the shard-key property.</param>
    /// <returns>A <see cref="ShardingBuilder{TEntity}"/> for chained configuration.</returns>
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
    /// Configures date-range sharding: rows are routed into time-bucketed shards
    /// (year, quarter, month, or day) by a date property.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="dateSelector">Expression selecting the date property.</param>
    /// <param name="interval">The bucketing interval (default: <see cref="DateShardInterval.Year"/>).</param>
    /// <returns>A <see cref="ShardingBuilder{TEntity}"/> for chained configuration.</returns>
    /// <example>
    /// <code>
    /// modelBuilder.Entity&lt;Order&gt;()
    ///     .ShardByDate(o =&gt; o.OrderDate, DateShardInterval.Year);
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
    /// Configures hash-based sharding for even distribution across a fixed shard count.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The shard-key property type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="shardKeySelector">Expression selecting the shard-key property.</param>
    /// <param name="shardCount">The total number of shards. Must be positive.</param>
    /// <returns>A <see cref="ShardingBuilder{TEntity}"/> for chained configuration.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="shardCount"/> is not positive.</exception>
    /// <example>
    /// <code>
    /// modelBuilder.Entity&lt;Product&gt;()
    ///     .ShardByHash(p =&gt; p.Id, shardCount: 8);
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shardCount);

        var shardKeyProperty = ExtractPropertyName(shardKeySelector);

        builder.Metadata.SetAnnotation(DtdeAnnotationNames.ShardKeyProperty, shardKeyProperty);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.ShardingStrategy, ShardingStrategyType.Hash);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.ShardCount, shardCount);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.IsSharded, true);
        builder.Metadata.SetAnnotation(DtdeAnnotationNames.StorageMode, ShardStorageMode.Tables);

        return new ShardingBuilder<TEntity>(builder);
    }

    /// <summary>
    /// Configures manual sharding with pre-existing tables, typically managed by a SQL project
    /// or DBA-owned process. Each table is mapped via a routing predicate.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="configureManual">Callback that registers the table mappings.</param>
    /// <returns>The same <see cref="EntityTypeBuilder{TEntity}"/> for further EF configuration.</returns>
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

    // ----------------------------------------------------------------------
    //  Temporal validity
    // ----------------------------------------------------------------------

    /// <summary>
    /// Configures bi-temporal validity tracking: queries can be filtered to a point-in-time, and
    /// new versions can be created without overwriting history. Property names are user-defined.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="validFromSelector">Expression selecting the validity-start property.</param>
    /// <param name="validToSelector">
    /// Optional expression selecting the validity-end property. Pass <see langword="null"/> for
    /// open-ended validity (no end date).
    /// </param>
    /// <returns>The same <see cref="EntityTypeBuilder{TEntity}"/> for further EF configuration.</returns>
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
    /// Overload of <see cref="HasTemporalValidity{TEntity}(EntityTypeBuilder{TEntity}, Expression{Func{TEntity, DateTime}}, Expression{Func{TEntity, DateTime?}})"/>
    /// for entities whose validity-end property is non-nullable.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="validFromSelector">Expression selecting the validity-start property.</param>
    /// <param name="validToSelector">Expression selecting the validity-end property.</param>
    /// <returns>The same <see cref="EntityTypeBuilder{TEntity}"/> for further EF configuration.</returns>
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
    /// Configures the temporal containment rule for parent-child relationships.
    /// Determines whether child validity must fall inside the parent's validity range.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="rule">The temporal containment rule.</param>
    /// <returns>The same <see cref="EntityTypeBuilder{TEntity}"/> for further EF configuration.</returns>
    public static EntityTypeBuilder<TEntity> HasTemporalContainment<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        TemporalContainmentRule rule)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Metadata.SetAnnotation(DtdeAnnotationNames.TemporalContainment, rule);

        return builder;
    }

    // ----------------------------------------------------------------------
    //  Helpers
    // ----------------------------------------------------------------------

    private static string ExtractPropertyName<TEntity, TProperty>(Expression<Func<TEntity, TProperty>> selector)
    {
        if (selector.Body is MemberExpression memberExpression)
        {
            return memberExpression.Member.Name;
        }

        if (selector.Body is UnaryExpression { Operand: MemberExpression unaryMemberExpression })
        {
            return unaryMemberExpression.Member.Name;
        }

        throw new ArgumentException(
            "Expression must be a property accessor (for example, x => x.PropertyName).",
            nameof(selector));
    }
}
