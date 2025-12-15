using Dtde.Abstractions.Transactions;

namespace Dtde.EntityFramework.Update;

/// <summary>
/// Handles temporal versioning and updates for entities.
/// </summary>
/// <remarks>
/// <para>
/// Most methods in this interface are now obsolete. With transparent sharding enabled,
/// use standard EF Core patterns instead:
/// </para>
/// <code>
/// // Instead of using IDtdeUpdateProcessor methods:
/// var newVersion = context.CreateNewVersion(entity, e => e.Name = "New", DateTime.Today);
/// await context.SaveChangesAsync(); // Automatically handles cross-shard
/// </code>
/// </remarks>
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
    /// <remarks>
    /// Consider using <see cref="DtdeDbContext.CreateNewVersion{TEntity}"/> with
    /// <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/>
    /// instead for transparent sharding support.
    /// </remarks>
    [Obsolete("Use DtdeDbContext.CreateNewVersion() followed by SaveChangesAsync() for transparent sharding support.")]
    Task<TEntity> CreateNewVersionAsync<TEntity>(
        TEntity currentEntity,
        Action<TEntity> changes,
        DateTime effectiveDate,
        CancellationToken cancellationToken = default) where TEntity : class, new();

    /// <summary>
    /// Processes an entity update by creating a new version within a cross-shard transaction.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="currentEntity">The current entity state.</param>
    /// <param name="changes">The action to apply changes to the new version.</param>
    /// <param name="effectiveDate">The date the new version becomes effective.</param>
    /// <param name="transaction">The cross-shard transaction to use.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new version of the entity.</returns>
    [Obsolete("Use standard EF Core transactions with transparent sharding instead.")]
    Task<TEntity> CreateNewVersionAsync<TEntity>(
        TEntity currentEntity,
        Action<TEntity> changes,
        DateTime effectiveDate,
        ICrossShardTransaction transaction,
        CancellationToken cancellationToken = default) where TEntity : class, new();

    /// <summary>
    /// Creates new versions for multiple entities in a single operation.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="updates">The collection of updates to apply.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new versions of the entities.</returns>
    [Obsolete("Use DtdeDbContext.CreateNewVersion() for each entity followed by SaveChangesAsync() for transparent sharding support.")]
    Task<IReadOnlyList<TEntity>> CreateNewVersionsAsync<TEntity>(
        IEnumerable<(TEntity CurrentEntity, Action<TEntity> Changes, DateTime EffectiveDate)> updates,
        CancellationToken cancellationToken = default) where TEntity : class, new();

    /// <summary>
    /// Terminates an entity by setting its valid-to date.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity to terminate.</param>
    /// <param name="terminationDate">The termination date.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    [Obsolete("Use DtdeDbContext.Terminate() followed by SaveChangesAsync() for transparent sharding support.")]
    Task TerminateVersionAsync<TEntity>(
        TEntity entity,
        DateTime terminationDate,
        CancellationToken cancellationToken = default) where TEntity : class;

    /// <summary>
    /// Terminates multiple entities in a single operation.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entities">The entities to terminate.</param>
    /// <param name="terminationDate">The termination date.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    [Obsolete("Use DtdeDbContext.Terminate() for each entity followed by SaveChangesAsync() for transparent sharding support.")]
    Task TerminateVersionsAsync<TEntity>(
        IEnumerable<TEntity> entities,
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
    [Obsolete("Use DtdeDbContext.AddTemporal() followed by SaveChangesAsync() for transparent sharding support.")]
    Task SaveNewEntityAsync<TEntity>(
        TEntity entity,
        DateTime effectiveFrom,
        CancellationToken cancellationToken = default) where TEntity : class;

    /// <summary>
    /// Saves multiple new entities in a single operation.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entities">The entities to save, each with their effective date.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    [Obsolete("Use DtdeDbContext.AddTemporal() for each entity followed by SaveChangesAsync() for transparent sharding support.")]
    Task SaveNewEntitiesAsync<TEntity>(
        IEnumerable<(TEntity Entity, DateTime EffectiveFrom)> entities,
        CancellationToken cancellationToken = default) where TEntity : class;

    /// <summary>
    /// Transfers an entity to a new shard by terminating it on the source shard
    /// and creating a new version on the target shard.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity to transfer.</param>
    /// <param name="targetShardId">The target shard identifier.</param>
    /// <param name="effectiveDate">The date the transfer becomes effective.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new version of the entity on the target shard.</returns>
    [Obsolete("Use DtdeDbContext.CreateNewVersion() followed by SaveChangesAsync() for transparent cross-shard transfers.")]
    Task<TEntity> TransferEntityAsync<TEntity>(
        TEntity entity,
        string targetShardId,
        DateTime effectiveDate,
        CancellationToken cancellationToken = default) where TEntity : class, new();
}
