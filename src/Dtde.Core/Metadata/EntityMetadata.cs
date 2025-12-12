using Dtde.Abstractions.Metadata;

namespace Dtde.Core.Metadata;

/// <summary>
/// Represents metadata configuration for a temporal and/or sharded entity.
/// </summary>
public sealed class EntityMetadata : IEntityMetadata
{
    /// <inheritdoc />
    public Type ClrType { get; }

    /// <inheritdoc />
    public string TableName { get; }

    /// <inheritdoc />
    public string SchemaName { get; }

    /// <inheritdoc />
    public IPropertyMetadata? PrimaryKey { get; }

    /// <inheritdoc />
    public IValidityConfiguration? Validity { get; }

    /// <inheritdoc />
    public IShardingConfiguration? Sharding { get; }

    /// <inheritdoc />
    public bool IsTemporal => Validity is not null;

    /// <inheritdoc />
    public bool IsSharded => Sharding is not null;

    /// <summary>
    /// Creates entity metadata with all configuration.
    /// </summary>
    /// <param name="clrType">The CLR type of the entity.</param>
    /// <param name="tableName">The database table name.</param>
    /// <param name="schemaName">The database schema name.</param>
    /// <param name="primaryKey">Optional primary key property metadata.</param>
    /// <param name="validity">Optional validity configuration.</param>
    /// <param name="sharding">Optional sharding configuration.</param>
    public EntityMetadata(
        Type clrType,
        string tableName,
        string schemaName,
        IPropertyMetadata? primaryKey = null,
        IValidityConfiguration? validity = null,
        IShardingConfiguration? sharding = null)
    {
        ClrType = clrType ?? throw new ArgumentNullException(nameof(clrType));
        TableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        SchemaName = schemaName ?? throw new ArgumentNullException(nameof(schemaName));
        PrimaryKey = primaryKey;
        Validity = validity;
        Sharding = sharding;
    }
}

/// <summary>
/// Builder for creating EntityMetadata instances.
/// </summary>
public sealed class EntityMetadataBuilder<TEntity> where TEntity : class
{
    private string _tableName;
    private string _schemaName = "dbo";
    private IPropertyMetadata? _primaryKey;
    private IValidityConfiguration? _validity;
    private IShardingConfiguration? _sharding;

    /// <summary>
    /// Creates a new entity metadata builder.
    /// </summary>
    public EntityMetadataBuilder()
    {
        _tableName = typeof(TEntity).Name;
    }

    /// <summary>
    /// Sets the table name for the entity.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <returns>The builder for chaining.</returns>
    public EntityMetadataBuilder<TEntity> ToTable(string tableName)
    {
        _tableName = tableName;
        return this;
    }

    /// <summary>
    /// Sets the schema name for the entity.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The builder for chaining.</returns>
    public EntityMetadataBuilder<TEntity> InSchema(string schemaName)
    {
        _schemaName = schemaName;
        return this;
    }

    /// <summary>
    /// Sets the primary key property.
    /// </summary>
    /// <param name="keySelector">Expression selecting the primary key property.</param>
    /// <returns>The builder for chaining.</returns>
    public EntityMetadataBuilder<TEntity> HasKey<TKey>(System.Linq.Expressions.Expression<Func<TEntity, TKey>> keySelector)
    {
        _primaryKey = PropertyMetadata.FromExpression(keySelector);
        return this;
    }

    /// <summary>
    /// Configures temporal validity with specified properties.
    /// </summary>
    /// <param name="validFromSelector">Expression selecting the validity start property.</param>
    /// <param name="validToSelector">Optional expression selecting the validity end property.</param>
    /// <returns>The builder for chaining.</returns>
    public EntityMetadataBuilder<TEntity> HasValidity(
        System.Linq.Expressions.Expression<Func<TEntity, DateTime>> validFromSelector,
        System.Linq.Expressions.Expression<Func<TEntity, DateTime?>>? validToSelector = null)
    {
        _validity = ValidityConfiguration.Create(validFromSelector, validToSelector);
        return this;
    }

    /// <summary>
    /// Configures temporal validity with non-nullable end date.
    /// </summary>
    /// <param name="validFromSelector">Expression selecting the validity start property.</param>
    /// <param name="validToSelector">Expression selecting the validity end property.</param>
    /// <returns>The builder for chaining.</returns>
    public EntityMetadataBuilder<TEntity> HasValidity(
        System.Linq.Expressions.Expression<Func<TEntity, DateTime>> validFromSelector,
        System.Linq.Expressions.Expression<Func<TEntity, DateTime>> validToSelector)
    {
        _validity = ValidityConfiguration.Create(validFromSelector, validToSelector);
        return this;
    }

    /// <summary>
    /// Configures temporal validity with property names.
    /// </summary>
    /// <param name="validFrom">The name of the validity start property.</param>
    /// <param name="validTo">The name of the validity end property.</param>
    /// <returns>The builder for chaining.</returns>
    public EntityMetadataBuilder<TEntity> HasTemporalValidity(string validFrom, string validTo)
    {
        _validity = ValidityConfiguration.Create<TEntity>(validFrom, validTo);
        return this;
    }

    /// <summary>
    /// Configures temporal validity with property names (start only).
    /// </summary>
    /// <param name="validFrom">The name of the validity start property.</param>
    /// <returns>The builder for chaining.</returns>
    public EntityMetadataBuilder<TEntity> HasTemporalValidity(string validFrom)
    {
        _validity = ValidityConfiguration.Create<TEntity>(validFrom, null);
        return this;
    }

    /// <summary>
    /// Sets the sharding configuration.
    /// </summary>
    /// <param name="sharding">The sharding configuration.</param>
    /// <returns>The builder for chaining.</returns>
    public EntityMetadataBuilder<TEntity> WithSharding(IShardingConfiguration sharding)
    {
        _sharding = sharding;
        return this;
    }

    /// <summary>
    /// Builds the EntityMetadata instance.
    /// </summary>
    /// <returns>The constructed EntityMetadata.</returns>
    public EntityMetadata Build()
    {
        // Try to auto-detect primary key if not specified
        var primaryKey = _primaryKey ?? TryDetectPrimaryKey();

        return new EntityMetadata(
            typeof(TEntity),
            _tableName,
            _schemaName,
            primaryKey,
            _validity,
            _sharding);
    }

    private static PropertyMetadata? TryDetectPrimaryKey()
    {
        var entityType = typeof(TEntity);

        // Convention: look for "Id" or "{EntityName}Id"
        var idProperty = entityType.GetProperty("Id")
                         ?? entityType.GetProperty($"{entityType.Name}Id");

        if (idProperty is not null)
        {
            return new PropertyMetadata(idProperty);
        }

        return null;
    }
}
