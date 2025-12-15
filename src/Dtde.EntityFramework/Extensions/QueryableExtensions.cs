namespace Dtde.EntityFramework.Extensions;

/// <summary>
/// Extension methods for IQueryable to add temporal filtering capabilities.
/// </summary>
public static class QueryableExtensions
{
    /// <summary>
    /// Filters the query to include only entities valid at the specified date.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The source queryable.</param>
    /// <param name="asOfDate">The point in time to filter by.</param>
    /// <returns>A queryable with temporal filtering applied.</returns>
    /// <example>
    /// <code>
    /// var activeContracts = await db.Contracts
    ///     .ValidAt(DateTime.Today)
    ///     .ToListAsync();
    /// </code>
    /// </example>
    public static IQueryable<TEntity> ValidAt<TEntity>(
        this IQueryable<TEntity> source,
        DateTime asOfDate) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);

        // This method is a marker that will be detected and processed by DTDE's expression rewriter
        // The actual temporal predicate injection happens in DtdeExpressionRewriter
        return source.Provider.CreateQuery<TEntity>(
            System.Linq.Expressions.Expression.Call(
                null,
                typeof(QueryableExtensions).GetMethod(nameof(ValidAt))!.MakeGenericMethod(typeof(TEntity)),
                source.Expression,
                System.Linq.Expressions.Expression.Constant(asOfDate)));
    }

    /// <summary>
    /// Filters the query to include only entities valid between the specified dates.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The source queryable.</param>
    /// <param name="startDate">The start of the date range.</param>
    /// <param name="endDate">The end of the date range.</param>
    /// <returns>A queryable with temporal range filtering applied.</returns>
    /// <example>
    /// <code>
    /// var contracts = await db.Contracts
    ///     .ValidBetween(startOfYear, endOfYear)
    ///     .ToListAsync();
    /// </code>
    /// </example>
    public static IQueryable<TEntity> ValidBetween<TEntity>(
        this IQueryable<TEntity> source,
        DateTime startDate,
        DateTime endDate) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Provider.CreateQuery<TEntity>(
            System.Linq.Expressions.Expression.Call(
                null,
                typeof(QueryableExtensions).GetMethod(nameof(ValidBetween))!.MakeGenericMethod(typeof(TEntity)),
                source.Expression,
                System.Linq.Expressions.Expression.Constant(startDate),
                System.Linq.Expressions.Expression.Constant(endDate)));
    }

    /// <summary>
    /// Includes all versions of entities, bypassing temporal filtering.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The source queryable.</param>
    /// <returns>A queryable that returns all versions.</returns>
    /// <example>
    /// <code>
    /// var allVersions = await db.Contracts
    ///     .WithVersions()
    ///     .Where(c => c.ContractNumber == "C001")
    ///     .ToListAsync();
    /// </code>
    /// </example>
    public static IQueryable<TEntity> WithVersions<TEntity>(
        this IQueryable<TEntity> source) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Provider.CreateQuery<TEntity>(
            System.Linq.Expressions.Expression.Call(
                null,
                typeof(QueryableExtensions).GetMethod(nameof(WithVersions))!.MakeGenericMethod(typeof(TEntity)),
                source.Expression));
    }

    /// <summary>
    /// Provides a hint to route the query to a specific shard.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The source queryable.</param>
    /// <param name="shardId">The target shard identifier.</param>
    /// <returns>A queryable with shard hint applied.</returns>
    /// <example>
    /// <code>
    /// var results = await db.Contracts
    ///     .ShardHint("Shard2024Q1")
    ///     .ToListAsync();
    /// </code>
    /// </example>
    public static IQueryable<TEntity> ShardHint<TEntity>(
        this IQueryable<TEntity> source,
        string shardId) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);

        return source.Provider.CreateQuery<TEntity>(
            System.Linq.Expressions.Expression.Call(
                null,
                typeof(QueryableExtensions).GetMethod(nameof(ShardHint))!.MakeGenericMethod(typeof(TEntity)),
                source.Expression,
                System.Linq.Expressions.Expression.Constant(shardId)));
    }
}
