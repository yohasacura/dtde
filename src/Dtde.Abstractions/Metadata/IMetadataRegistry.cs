namespace Dtde.Abstractions.Metadata;

/// <summary>
/// Result of metadata validation.
/// </summary>
public sealed class MetadataValidationResult
{
    /// <summary>
    /// Gets whether the validation succeeded without errors.
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// Gets the validation errors.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Gets the validation warnings.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static MetadataValidationResult Success() => new([], []);

    /// <summary>
    /// Creates a validation result with errors.
    /// </summary>
    /// <param name="errors">The validation errors.</param>
    /// <param name="warnings">Optional validation warnings.</param>
    public static MetadataValidationResult Failure(IEnumerable<string> errors, IEnumerable<string>? warnings = null)
        => new([.. errors], warnings is not null ? [.. warnings] : []);

    private MetadataValidationResult(IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
    {
        Errors = errors;
        Warnings = warnings;
    }
}

/// <summary>
/// Central registry for all DTDE metadata.
/// Provides access to entity, relation, and shard configurations.
/// </summary>
public interface IMetadataRegistry
{
    /// <summary>
    /// Gets entity metadata by CLR type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <returns>Entity metadata if configured, null otherwise.</returns>
    IEntityMetadata? GetEntityMetadata<TEntity>() where TEntity : class;

    /// <summary>
    /// Gets entity metadata by CLR type.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <returns>Entity metadata if configured, null otherwise.</returns>
    IEntityMetadata? GetEntityMetadata(Type entityType);

    /// <summary>
    /// Gets all registered entity metadata.
    /// </summary>
    /// <returns>All entity metadata.</returns>
    IReadOnlyList<IEntityMetadata> GetAllEntityMetadata();

    /// <summary>
    /// Gets relations for an entity type.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <returns>Relations where the entity is parent or child.</returns>
    IReadOnlyList<IRelationMetadata> GetRelations(Type entityType);

    /// <summary>
    /// Gets the shard registry.
    /// </summary>
    IShardRegistry ShardRegistry { get; }

    /// <summary>
    /// Validates all registered metadata for consistency.
    /// </summary>
    /// <returns>Validation result with any errors or warnings.</returns>
    MetadataValidationResult Validate();
}
