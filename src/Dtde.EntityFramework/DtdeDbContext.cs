using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Temporal;
using Dtde.Core.Metadata;
using Dtde.Core.Temporal;
using Dtde.EntityFramework.Configuration;
using Dtde.EntityFramework.Infrastructure;
using Dtde.EntityFramework.Query;
using Dtde.EntityFramework.Update;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Dtde.EntityFramework;

/// <summary>
/// Base DbContext that provides DTDE temporal and sharding functionality.
/// </summary>
public abstract class DtdeDbContext : DbContext
{
    private ITemporalContext? _temporalContext;
    private IMetadataRegistry? _metadataRegistry;
    private IShardRegistry? _shardRegistry;
    private DtdeOptions? _options;
    private bool _registryBackfilled;
    private bool _modelCreating;

    /// <summary>
    /// Initializes a new instance of the <see cref="DtdeDbContext"/> class.
    /// </summary>
    protected DtdeDbContext()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DtdeDbContext"/> class.
    /// </summary>
    /// <param name="options">The DbContext options.</param>
    protected DtdeDbContext(DbContextOptions options) : base(options)
    {
    }

    /// <summary>
    /// Gets the temporal context for configuring query time.
    /// </summary>
    public ITemporalContext TemporalContext => _temporalContext ??= GetDtdeService<ITemporalContext>();

    /// <summary>
    /// Gets the metadata registry. The first read after model construction
    /// also lifts any DTDE annotations declared in <c>OnModelCreating</c> into
    /// the registry, so application code only ever has to configure entities
    /// once (in <c>OnModelCreating</c>).
    /// </summary>
    public IMetadataRegistry MetadataRegistry
    {
        get
        {
            _metadataRegistry ??= GetDtdeService<IMetadataRegistry>();
            EnsureRegistryBackfilled();
            return _metadataRegistry;
        }
    }

    /// <summary>
    /// Once-per-context backfill: scans the EF Core <see cref="DbContext.Model"/>
    /// for DTDE annotations and registers a corresponding <see cref="EntityMetadata"/>
    /// for each entity not already present in the registry. Idempotent.
    /// </summary>
    private void EnsureRegistryBackfilled()
    {
        if (_registryBackfilled
            || _modelCreating
            || _metadataRegistry is not MetadataRegistry concreteRegistry)
        {
            return;
        }

        _registryBackfilled = true;

        foreach (var entityType in Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (concreteRegistry.GetEntityMetadata(clrType) is not null)
            {
                continue;
            }

            var temporal = ReadTemporalConfiguration(entityType, clrType);
            if (temporal is null)
            {
                continue;
            }

            var tableName = entityType.GetTableName() ?? clrType.Name;
            var schemaName = entityType.GetSchema() ?? "dbo";
            var primaryKey = ReadPrimaryKey(entityType, clrType);

            concreteRegistry.RegisterEntity(new EntityMetadata(
                clrType,
                tableName,
                schemaName,
                primaryKey: primaryKey,
                temporalConfiguration: temporal,
                shardingConfiguration: null));
        }
    }

    private static IPropertyMetadata? ReadPrimaryKey(IReadOnlyEntityType entityType, Type clrType)
    {
        var primaryKey = entityType.FindPrimaryKey();
        if (primaryKey is null || primaryKey.Properties.Count != 1)
        {
            return null;
        }

        var keyName = primaryKey.Properties[0].Name;
        var keyInfo = clrType.GetProperty(keyName);
        return keyInfo is not null ? new PropertyMetadata(keyInfo) : null;
    }

    private static ITemporalConfiguration? ReadTemporalConfiguration(IReadOnlyEntityType entityType, Type clrType)
    {
        var isTemporal = entityType.FindAnnotation(DtdeAnnotationNames.IsTemporal)?.Value as bool? ?? false;
        if (!isTemporal)
        {
            return null;
        }

        var validFromName = entityType.FindAnnotation(DtdeAnnotationNames.ValidFromProperty)?.Value as string;
        if (string.IsNullOrWhiteSpace(validFromName))
        {
            return null;
        }

        var validFromInfo = clrType.GetProperty(validFromName);
        if (validFromInfo is null)
        {
            return null;
        }

        var validToName = entityType.FindAnnotation(DtdeAnnotationNames.ValidToProperty)?.Value as string;
        var validToInfo = !string.IsNullOrWhiteSpace(validToName)
            ? clrType.GetProperty(validToName)
            : null;

        return new TemporalConfiguration(
            new PropertyMetadata(validFromInfo),
            validToInfo is not null ? new PropertyMetadata(validToInfo) : null);
    }

    /// <summary>
    /// Gets the shard registry.
    /// </summary>
    public IShardRegistry ShardRegistry => _shardRegistry ??= GetDtdeService<IShardRegistry>();

    /// <summary>
    /// Gets the DTDE options.
    /// </summary>
    protected DtdeOptions DtdeOptions => _options ??= GetDtdeService<DtdeOptions>();

    /// <summary>
    /// Gets a queryable filtered to entities valid at the specified date.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="asOfDate">The point in time.</param>
    /// <returns>A queryable filtered to valid entities.</returns>
    public IQueryable<TEntity> ValidAt<TEntity>(DateTime asOfDate) where TEntity : class
    {
        var metadata = MetadataRegistry.GetEntityMetadata<TEntity>();
        if (metadata?.TemporalConfiguration is null)
        {
            return Set<TEntity>().AsQueryable();
        }

        var predicate = metadata.TemporalConfiguration.BuildPredicate<TEntity>(asOfDate);
        return Set<TEntity>().Where(predicate);
    }

    /// <summary>
    /// Gets a queryable filtered to entities valid within the specified date range.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="startDate">The start of the range.</param>
    /// <param name="endDate">The end of the range.</param>
    /// <returns>A queryable filtered to valid entities.</returns>
    public IQueryable<TEntity> ValidBetween<TEntity>(DateTime startDate, DateTime endDate) where TEntity : class
    {
        var metadata = MetadataRegistry.GetEntityMetadata<TEntity>();
        if (metadata?.TemporalConfiguration is null)
        {
            return Set<TEntity>().AsQueryable();
        }

        // ValidFrom <= endDate AND (ValidTo IS NULL OR ValidTo >= startDate)
        return BuildRangeQuery<TEntity>(metadata.TemporalConfiguration, startDate, endDate);
    }

    /// <summary>
    /// Gets all versions of entities, bypassing temporal filtering.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <returns>A queryable with all entity versions.</returns>
    public IQueryable<TEntity> AllVersions<TEntity>() where TEntity : class
    {
        return Set<TEntity>().AsQueryable();
    }

    /// <summary>
    /// Creates a new temporal version of an entity.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="currentEntity">The current entity to version.</param>
    /// <param name="changes">The changes to apply to the new version.</param>
    /// <param name="effectiveDate">The date the new version becomes effective.</param>
    /// <returns>The new version of the entity.</returns>
    public TEntity CreateNewVersion<TEntity>(
        TEntity currentEntity,
        Action<TEntity> changes,
        DateTime effectiveDate) where TEntity : class, new()
    {
        ArgumentNullException.ThrowIfNull(changes);

        var versionManager = new VersionManager(MetadataRegistry);

        // Get current validity
        var (currentValidFrom, _) = versionManager.GetValidityPeriod(currentEntity);

        if (effectiveDate <= currentValidFrom)
        {
            throw new InvalidOperationException(
                $"New version effective date must be after {currentValidFrom}.");
        }

        // Terminate current version
        versionManager.TerminateVersion(currentEntity, effectiveDate.AddTicks(-1));
        Update(currentEntity);

        // Create and configure new version
        var newVersion = versionManager.CreateVersion(currentEntity, effectiveDate);
        changes(newVersion);
        Add(newVersion);

        return newVersion;
    }

    /// <summary>
    /// Terminates an entity as of the specified date.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity to terminate.</param>
    /// <param name="terminationDate">The termination date.</param>
    public void Terminate<TEntity>(TEntity entity, DateTime terminationDate) where TEntity : class
    {
        var versionManager = new VersionManager(MetadataRegistry);
        versionManager.TerminateVersion(entity, terminationDate);
        Update(entity);
    }

    /// <summary>
    /// Adds a new temporal entity with the specified effective date.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity to add.</param>
    /// <param name="effectiveFrom">The date the entity becomes valid.</param>
    public void AddTemporal<TEntity>(TEntity entity, DateTime effectiveFrom) where TEntity : class
    {
        var versionManager = new VersionManager(MetadataRegistry);
        versionManager.InitializeValidity(entity, effectiveFrom);
        Add(entity);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        _modelCreating = true;
        try
        {
            base.OnModelCreating(modelBuilder);

            // Reverse direction: any entity already declared via the (advanced)
            // registry path has its annotations applied to the model. Forward
            // direction — lifting OnModelCreating annotations into the registry —
            // happens lazily on first MetadataRegistry access (see
            // EnsureRegistryBackfilled), because the user's HasTemporalValidity /
            // ShardBy* calls happen after base.OnModelCreating returns.
            ApplyDtdeConfigurations(modelBuilder);
        }
        finally
        {
            _modelCreating = false;
        }
    }

    /// <summary>
    /// Configures temporal entities using the fluent API.
    /// Override this method to configure your temporal entities.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    protected virtual void ConfigureTemporalEntities(ModelBuilder modelBuilder)
    {
    }

    private void ApplyDtdeConfigurations(ModelBuilder modelBuilder)
    {
        // Get all entity metadata from the registry
        foreach (var metadata in MetadataRegistry.GetAllEntityMetadata())
        {
            var entityBuilder = modelBuilder.Entity(metadata.ClrType);

            // Configure key if specified
            if (metadata.PrimaryKey is not null)
            {
                entityBuilder.HasKey(metadata.PrimaryKey.PropertyName);
            }

            // Add annotations for temporal configuration
            if (metadata.TemporalConfiguration is not null)
            {
                entityBuilder.HasAnnotation(
                    DtdeAnnotationNames.IsTemporal,
                    true);
                entityBuilder.HasAnnotation(
                    DtdeAnnotationNames.ValidFromProperty,
                    metadata.TemporalConfiguration.ValidFromProperty.PropertyName);

                if (metadata.TemporalConfiguration.ValidToProperty is not null)
                {
                    entityBuilder.HasAnnotation(
                        DtdeAnnotationNames.ValidToProperty,
                        metadata.TemporalConfiguration.ValidToProperty.PropertyName);
                }
            }

            // Add annotations for sharding configuration
            if (metadata.ShardingConfiguration is not null)
            {
                entityBuilder.HasAnnotation(
                    DtdeAnnotationNames.IsSharded,
                    true);
                entityBuilder.HasAnnotation(
                    DtdeAnnotationNames.ShardingStrategy,
                    metadata.ShardingConfiguration.StrategyType.ToString());

                var keyProperties = string.Join(",",
                    metadata.ShardingConfiguration.ShardKeyProperties.Select(p => p.PropertyName));
                entityBuilder.HasAnnotation(
                    DtdeAnnotationNames.ShardKeyProperty,
                    keyProperties);
            }
        }

        ConfigureTemporalEntities(modelBuilder);
    }

    private IQueryable<TEntity> BuildRangeQuery<TEntity>(
        ITemporalConfiguration config,
        DateTime startDate,
        DateTime endDate) where TEntity : class
    {
        var query = Set<TEntity>().AsQueryable();

        // Build predicate: ValidFrom <= endDate AND (ValidTo IS NULL OR ValidTo >= startDate)
        var validFromProperty = typeof(TEntity).GetProperty(config.ValidFromProperty.PropertyName)!;
        var validToProperty = config.ValidToProperty is not null
            ? typeof(TEntity).GetProperty(config.ValidToProperty.PropertyName)
            : null;

        var parameter = System.Linq.Expressions.Expression.Parameter(typeof(TEntity), "e");

        // ValidFrom <= endDate
        var validFromAccess = System.Linq.Expressions.Expression.Property(parameter, validFromProperty);
        var endDateConstant = System.Linq.Expressions.Expression.Constant(endDate);
        var validFromCheck = System.Linq.Expressions.Expression.LessThanOrEqual(validFromAccess, endDateConstant);

        System.Linq.Expressions.Expression body;

        if (validToProperty is not null)
        {
            var validToAccess = System.Linq.Expressions.Expression.Property(parameter, validToProperty);
            var startDateConstant = System.Linq.Expressions.Expression.Constant(startDate);

            if (validToProperty.PropertyType == typeof(DateTime?))
            {
                var hasValue = System.Linq.Expressions.Expression.Property(validToAccess, "HasValue");
                var value = System.Linq.Expressions.Expression.Property(validToAccess, "Value");
                var nullCheck = System.Linq.Expressions.Expression.Not(hasValue);
                var validToGreaterOrEqual = System.Linq.Expressions.Expression.GreaterThanOrEqual(value, startDateConstant);
                var validToCheck = System.Linq.Expressions.Expression.OrElse(nullCheck, validToGreaterOrEqual);
                body = System.Linq.Expressions.Expression.AndAlso(validFromCheck, validToCheck);
            }
            else
            {
                var validToCheck = System.Linq.Expressions.Expression.GreaterThanOrEqual(validToAccess, startDateConstant);
                body = System.Linq.Expressions.Expression.AndAlso(validFromCheck, validToCheck);
            }
        }
        else
        {
            body = validFromCheck;
        }

        var lambda = System.Linq.Expressions.Expression.Lambda<Func<TEntity, bool>>(body, parameter);
        return query.Where(lambda);
    }

    private TService GetDtdeService<TService>() where TService : class
    {
        var extension = this.GetService<IDbContextOptions>()
            .FindExtension<DtdeOptionsExtension>() ?? throw new InvalidOperationException(
                "DTDE is not configured. Call UseDtde() in your DbContext configuration.");
        object service = typeof(TService).Name switch
        {
            nameof(ITemporalContext) => extension.Options.TemporalContext,
            nameof(IMetadataRegistry) => extension.Options.MetadataRegistry,
            nameof(IShardRegistry) => extension.Options.ShardRegistry,
            nameof(DtdeOptions) => extension.Options,
            _ => throw new InvalidOperationException($"Unknown DTDE service type: {typeof(TService).Name}")
        };
        return (TService)service;
    }
}
