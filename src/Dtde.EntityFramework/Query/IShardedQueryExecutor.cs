namespace Dtde.EntityFramework.Query;

/// <summary>
/// Provides functionality to execute queries across multiple shards and merge results.
/// </summary>
public interface IShardedQueryExecutor
{
    /// <summary>
    /// Executes a query across relevant shards.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="query">The query to execute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The merged results from all shards.</returns>
    Task<IReadOnlyList<TEntity>> ExecuteAsync<TEntity>(
        IQueryable<TEntity> query,
        CancellationToken cancellationToken = default) where TEntity : class;

    /// <summary>
    /// Executes a scalar query across relevant shards.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="query">The query to execute.</param>
    /// <param name="aggregator">The function to aggregate results from shards.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The aggregated result.</returns>
    Task<TResult> ExecuteScalarAsync<TEntity, TResult>(
        IQueryable<TEntity> query,
        Func<IEnumerable<TResult>, TResult> aggregator,
        CancellationToken cancellationToken = default) where TEntity : class;
}
