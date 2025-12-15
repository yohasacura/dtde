using Dtde.Abstractions.Metadata;

namespace Dtde.EntityFramework.Update;

/// <summary>
/// Manages temporal versions of entities, handling version creation and termination.
/// </summary>
public sealed class VersionManager
{
    private readonly IMetadataRegistry _metadataRegistry;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionManager"/> class.
    /// </summary>
    /// <param name="metadataRegistry">The metadata registry.</param>
    public VersionManager(IMetadataRegistry metadataRegistry)
    {
        _metadataRegistry = metadataRegistry ?? throw new ArgumentNullException(nameof(metadataRegistry));
    }

    /// <summary>
    /// Creates a new version of an entity by cloning it and setting temporal properties.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The source entity to version.</param>
    /// <param name="effectiveFrom">The effective date for the new version.</param>
    /// <returns>A new entity instance representing the new version.</returns>
    public TEntity CreateVersion<TEntity>(TEntity source, DateTime effectiveFrom) where TEntity : class, new()
    {
        ArgumentNullException.ThrowIfNull(source);

        var metadata = _metadataRegistry.GetEntityMetadata<TEntity>();
        if (metadata?.ValidityConfiguration is null)
        {
            throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' is not configured with temporal validity.");
        }

        // Clone the entity
        var newVersion = CloneEntity(source, metadata);

        // Set ValidFrom on new version
        metadata.ValidityConfiguration.ValidFromProperty.SetValue(newVersion, effectiveFrom);

        // Clear ValidTo on new version (it's currently valid)
        metadata.ValidityConfiguration.ValidToProperty?.SetValue(newVersion, null);

        return newVersion;
    }

    /// <summary>
    /// Terminates an entity version by setting its ValidTo date.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity to terminate.</param>
    /// <param name="terminationDate">The termination date.</param>
    public void TerminateVersion<TEntity>(TEntity entity, DateTime terminationDate) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);

        var metadata = _metadataRegistry.GetEntityMetadata<TEntity>();
        if (metadata?.ValidityConfiguration?.ValidToProperty is null)
        {
            throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' does not have a ValidTo property configured.");
        }

        // Ensure termination date is after or equal to ValidFrom
        var validFrom = (DateTime)metadata.ValidityConfiguration.ValidFromProperty.GetValue(entity)!;
        if (terminationDate < validFrom)
        {
            throw new ArgumentException(
                "Termination date cannot be before the entity's ValidFrom date.",
                nameof(terminationDate));
        }

        metadata.ValidityConfiguration.ValidToProperty.SetValue(entity, terminationDate);
    }

    /// <summary>
    /// Sets the initial validity for a new entity.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity to initialize.</param>
    /// <param name="effectiveFrom">The effective date.</param>
    public void InitializeValidity<TEntity>(TEntity entity, DateTime effectiveFrom) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);

        var metadata = _metadataRegistry.GetEntityMetadata<TEntity>();
        if (metadata?.ValidityConfiguration is null)
        {
            throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' is not configured with temporal validity.");
        }

        metadata.ValidityConfiguration.ValidFromProperty.SetValue(entity, effectiveFrom);
        metadata.ValidityConfiguration.ValidToProperty?.SetValue(entity, null);
    }

    /// <summary>
    /// Checks if two date ranges overlap.
    /// </summary>
    /// <param name="start1">Start of first range.</param>
    /// <param name="end1">End of first range (null means open-ended).</param>
    /// <param name="start2">Start of second range.</param>
    /// <param name="end2">End of second range (null means open-ended).</param>
    /// <returns>True if the ranges overlap.</returns>
    public static bool RangesOverlap(DateTime start1, DateTime? end1, DateTime start2, DateTime? end2)
    {
        var effectiveEnd1 = end1 ?? DateTime.MaxValue;
        var effectiveEnd2 = end2 ?? DateTime.MaxValue;

        return start1 < effectiveEnd2 && start2 < effectiveEnd1;
    }

    /// <summary>
    /// Gets the validity period for an entity.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity.</param>
    /// <returns>A tuple of ValidFrom and ValidTo dates.</returns>
    public (DateTime ValidFrom, DateTime? ValidTo) GetValidityPeriod<TEntity>(TEntity entity) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);

        var metadata = _metadataRegistry.GetEntityMetadata<TEntity>();
        if (metadata?.ValidityConfiguration is null)
        {
            throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' is not configured with temporal validity.");
        }

        var validFrom = (DateTime)metadata.ValidityConfiguration.ValidFromProperty.GetValue(entity)!;
        var validTo = metadata.ValidityConfiguration.ValidToProperty?.GetValue(entity) as DateTime?;

        return (validFrom, validTo);
    }

    private static TEntity CloneEntity<TEntity>(TEntity source, IEntityMetadata metadata) where TEntity : class, new()
    {
        var clone = new TEntity();

        // Copy all property values except key properties (which should be new)
        foreach (var property in typeof(TEntity).GetProperties())
        {
            if (!property.CanRead || !property.CanWrite)
            {
                continue;
            }

            // Skip key properties - they should be handled by the database
            if (metadata.KeyProperty?.PropertyName == property.Name)
            {
                continue;
            }

            var value = property.GetValue(source);
            property.SetValue(clone, value);
        }

        return clone;
    }
}
