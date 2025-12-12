namespace Dtde.EntityFramework.Update;

/// <summary>
/// Handles temporal versioning and updates for entities.
/// </summary>
public interface IDtdeUpdateProcessor
{
    /// <summary>
    /// Processes an entity update by creating a new version.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="currentEntity">The current entity state.</param>
    /// <param name="changes">The action to apply changes to the new version.</param>
    /// <param name="effectiveDate">The date the new version becomes effective.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new version of the entity.</returns>
    Task<TEntity> CreateNewVersionAsync<TEntity>(
        TEntity currentEntity,
        Action<TEntity> changes,
        DateTime effectiveDate,
        CancellationToken cancellationToken = default) where TEntity : class, new();

    /// <summary>
    /// Terminates an entity by setting its valid-to date.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity to terminate.</param>
    /// <param name="terminationDate">The termination date.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task TerminateVersionAsync<TEntity>(
        TEntity entity,
        DateTime terminationDate,
        CancellationToken cancellationToken = default) where TEntity : class;

    /// <summary>
    /// Saves a new entity with temporal validity.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity to save.</param>
    /// <param name="effectiveFrom">The date the entity becomes valid.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task SaveNewEntityAsync<TEntity>(
        TEntity entity,
        DateTime effectiveFrom,
        CancellationToken cancellationToken = default) where TEntity : class;
}
