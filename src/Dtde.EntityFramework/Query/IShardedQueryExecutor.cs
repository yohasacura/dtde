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
    public Task<IReadOnlyList<TEntity>> ExecuteAsync<TEntity>(
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
    public Task<TResult> ExecuteScalarAsync<TEntity, TResult>(
        IQueryable<TEntity> query,
        Func<IEnumerable<TResult>, TResult> aggregator,
        CancellationToken cancellationToken = default) where TEntity : class;

    /// <summary>
    /// Streams a query's results across shards as an
    /// <see cref="IAsyncEnumerable{T}"/>. Each shard's result set is pulled
    /// concurrently into a bounded buffer; consumers see entities in arrival
    /// order (across shards) so the call uses constant memory regardless of
    /// the result-set size.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="query">The query to execute.</param>
    /// <param name="bufferSize">
    /// Maximum number of entities buffered in flight before producers wait.
    /// Defaults to <c>shardCount * 64</c>; minimum 16.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An async stream of entities. Order is not guaranteed —
    /// apply <c>OrderBy</c> on the result if you need it.</returns>
    public IAsyncEnumerable<TEntity> ExecuteStreamingAsync<TEntity>(
        IQueryable<TEntity> query,
        int? bufferSize = null,
        CancellationToken cancellationToken = default) where TEntity : class;
}
