using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Temporal;

using Dtde.Core.Temporal;

namespace Dtde.Core.Metadata;

/// <summary>
/// Default <see cref="IEntityMetadata"/> implementation. Use
/// <see cref="EntityMetadataBuilder{TEntity}"/> to construct instances fluently.
/// </summary>
public sealed class EntityMetadata : IEntityMetadata
{
    /// <summary>
    /// Creates entity metadata with all configuration.
    /// </summary>
    /// <param name="clrType">The CLR type of the entity.</param>
    /// <param name="tableName">The database table name.</param>
    /// <param name="schemaName">The database schema name.</param>
    /// <param name="primaryKey">Optional primary-key property metadata.</param>
    /// <param name="temporalConfiguration">Optional temporal configuration.</param>
    /// <param name="shardingConfiguration">Optional sharding configuration.</param>
    public EntityMetadata(
        Type clrType,
        string tableName,
        string schemaName,
        IPropertyMetadata? primaryKey = null,
        ITemporalConfiguration? temporalConfiguration = null,
        IShardingConfiguration? shardingConfiguration = null)
    {
        ClrType = clrType ?? throw new ArgumentNullException(nameof(clrType));
        TableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        SchemaName = schemaName ?? throw new ArgumentNullException(nameof(schemaName));
        PrimaryKey = primaryKey;
        TemporalConfiguration = temporalConfiguration;
        ShardingConfiguration = shardingConfiguration;
    }

    /// <inheritdoc />
    public Type ClrType { get; }

    /// <inheritdoc />
    public string TableName { get; }

    /// <inheritdoc />
    public string SchemaName { get; }

    /// <inheritdoc />
    public IPropertyMetadata? PrimaryKey { get; }

    /// <inheritdoc />
    public ITemporalConfiguration? TemporalConfiguration { get; }

    /// <inheritdoc />
    public IShardingConfiguration? ShardingConfiguration { get; }

    /// <inheritdoc />
    public bool IsTemporal => TemporalConfiguration is not null;

    /// <inheritdoc />
    public bool IsSharded => ShardingConfiguration is not null;
}

/// <summary>
/// Internal fluent builder for <see cref="EntityMetadata"/>. Application code
/// should not use this directly — entity sharding/temporal configuration belongs
/// in <c>DbContext.OnModelCreating</c> via the <c>EntityTypeBuilder&lt;T&gt;</c>
/// extension methods (<c>ShardBy</c>, <c>HasTemporalValidity</c>, etc.).
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
internal sealed class EntityMetadataBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TEntity>
    where TEntity : class
{
    private string _tableName;
    private string _schemaName = "dbo";
    private IPropertyMetadata? _primaryKey;
    private ITemporalConfiguration? _temporalConfiguration;
    private IShardingConfiguration? _shardingConfiguration;

    /// <summary>
    /// Creates a new builder. The table name defaults to the entity's CLR name.
    /// </summary>
    public EntityMetadataBuilder()
    {
        _tableName = typeof(TEntity).Name;
    }

    /// <summary>
    /// Sets the table name.
    /// </summary>
    public EntityMetadataBuilder<TEntity> ToTable(string tableName)
    {
        ArgumentException.ThrowIfNullOrEmpty(tableName);

        _tableName = tableName;
        return this;
    }

    /// <summary>
    /// Sets the schema name.
    /// </summary>
    public EntityMetadataBuilder<TEntity> InSchema(string schemaName)
    {
        ArgumentException.ThrowIfNullOrEmpty(schemaName);

        _schemaName = schemaName;
        return this;
    }

    /// <summary>
    /// Sets the primary-key property by selector expression.
    /// </summary>
    public EntityMetadataBuilder<TEntity> HasKey<TKey>(Expression<Func<TEntity, TKey>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);

        _primaryKey = PropertyMetadata.FromExpression(keySelector);
        return this;
    }

    /// <summary>
    /// Configures bi-temporal validity by property selectors. Pass <see langword="null"/>
    /// for <paramref name="validToSelector"/> to declare an open-ended entity.
    /// </summary>
    public EntityMetadataBuilder<TEntity> HasValidity(
        Expression<Func<TEntity, DateTime>> validFromSelector,
        Expression<Func<TEntity, DateTime?>>? validToSelector = null)
    {
        ArgumentNullException.ThrowIfNull(validFromSelector);

        _temporalConfiguration = TemporalConfiguration.Create(validFromSelector, validToSelector);
        return this;
    }

    /// <summary>
    /// Configures bi-temporal validity by property selectors with a non-nullable end date.
    /// </summary>
    public EntityMetadataBuilder<TEntity> HasValidity(
        Expression<Func<TEntity, DateTime>> validFromSelector,
        Expression<Func<TEntity, DateTime>> validToSelector)
    {
        ArgumentNullException.ThrowIfNull(validFromSelector);
        ArgumentNullException.ThrowIfNull(validToSelector);

        _temporalConfiguration = TemporalConfiguration.Create(validFromSelector, validToSelector);
        return this;
    }

    /// <summary>
    /// Configures bi-temporal validity by property names.
    /// </summary>
    [RequiresUnreferencedCode("Looks up properties by name on TEntity. Annotate the calling generic with DynamicallyAccessedMembers=PublicProperties when used in trim/AOT scenarios.")]
    public EntityMetadataBuilder<TEntity> HasTemporalValidity(string validFrom, string validTo)
    {
        ArgumentException.ThrowIfNullOrEmpty(validFrom);
        ArgumentException.ThrowIfNullOrEmpty(validTo);

        _temporalConfiguration = TemporalConfiguration.Create<TEntity>(validFrom, validTo);
        return this;
    }

    /// <summary>
    /// Configures open-ended bi-temporal validity by property name (start only).
    /// </summary>
    [RequiresUnreferencedCode("Looks up properties by name on TEntity. Annotate the calling generic with DynamicallyAccessedMembers=PublicProperties when used in trim/AOT scenarios.")]
    public EntityMetadataBuilder<TEntity> HasTemporalValidity(string validFrom)
    {
        ArgumentException.ThrowIfNullOrEmpty(validFrom);

        _temporalConfiguration = TemporalConfiguration.Create<TEntity>(validFrom, null);
        return this;
    }

    /// <summary>
    /// Sets the sharding configuration.
    /// </summary>
    public EntityMetadataBuilder<TEntity> WithSharding(IShardingConfiguration sharding)
    {
        ArgumentNullException.ThrowIfNull(sharding);

        _shardingConfiguration = sharding;
        return this;
    }

    /// <summary>
    /// Builds the <see cref="EntityMetadata"/> instance. Auto-detects the primary key
    /// (looking for <c>Id</c> or <c>{EntityName}Id</c>) when one was not explicitly set.
    /// </summary>
    public EntityMetadata Build()
    {
        var primaryKey = _primaryKey ?? TryDetectPrimaryKey();

        return new EntityMetadata(
            typeof(TEntity),
            _tableName,
            _schemaName,
            primaryKey,
            _temporalConfiguration,
            _shardingConfiguration);
    }

    private static PropertyMetadata? TryDetectPrimaryKey()
    {
        var entityType = typeof(TEntity);

        var idProperty = entityType.GetProperty("Id")
                         ?? entityType.GetProperty($"{entityType.Name}Id");

        return idProperty is not null
            ? new PropertyMetadata(idProperty)
            : null;
    }
}
