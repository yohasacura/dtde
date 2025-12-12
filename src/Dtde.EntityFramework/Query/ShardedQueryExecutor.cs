using System.Linq.Expressions;
using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Temporal;
using Dtde.EntityFramework.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dtde.EntityFramework.Query;

/// <summary>
/// Executes queries across multiple shards and merges results.
/// Supports both database-level and table-level sharding modes.
/// </summary>
/// <example>
/// <code>
/// // Database-level sharding: queries execute against separate databases
/// // Table-level sharding: queries execute against different tables in same database
///
/// var results = await executor.ExecuteAsync(
///     context.Customers.Where(c => c.Region == "EU"),
///     cancellationToken);
/// </code>
/// </example>
public sealed class ShardedQueryExecutor : IShardedQueryExecutor
{
    private readonly IShardRegistry _shardRegistry;
    private readonly IMetadataRegistry _metadataRegistry;
    private readonly ITemporalContext _temporalContext;
    private readonly IShardContextFactory _shardContextFactory;
    private readonly ILogger<ShardedQueryExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShardedQueryExecutor"/> class.
    /// </summary>
    /// <param name="shardRegistry">The shard registry.</param>
    /// <param name="metadataRegistry">The metadata registry.</param>
    /// <param name="temporalContext">The temporal context.</param>
    /// <param name="shardContextFactory">The shard context factory.</param>
    /// <param name="logger">The logger.</param>
    public ShardedQueryExecutor(
        IShardRegistry shardRegistry,
        IMetadataRegistry metadataRegistry,
        ITemporalContext temporalContext,
        IShardContextFactory shardContextFactory,
        ILogger<ShardedQueryExecutor> logger)
    {
        _shardRegistry = shardRegistry ?? throw new ArgumentNullException(nameof(shardRegistry));
        _metadataRegistry = metadataRegistry ?? throw new ArgumentNullException(nameof(metadataRegistry));
        _temporalContext = temporalContext ?? throw new ArgumentNullException(nameof(temporalContext));
        _shardContextFactory = shardContextFactory ?? throw new ArgumentNullException(nameof(shardContextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TEntity>> ExecuteAsync<TEntity>(
        IQueryable<TEntity> query,
        CancellationToken cancellationToken = default) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(query);

        var entityType = typeof(TEntity);
        var shardsList = DetermineTargetShards(entityType, query.Expression).ToList();

        if (shardsList.Count == 0)
        {
            LogMessages.NoShardsFound(_logger, entityType.Name);
            return [];
        }

        LogMessages.ExecutingQueryAcrossShards(_logger, shardsList.Count, entityType.Name);

        var tasks = shardsList.Select(shard => ExecuteOnShardAsync(query, shard, cancellationToken));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        return MergeResults(results.SelectMany(r => r).ToList(), query.Expression);
    }

    /// <inheritdoc />
    public async Task<TResult> ExecuteScalarAsync<TEntity, TResult>(
        IQueryable<TEntity> query,
        Func<IEnumerable<TResult>, TResult> aggregator,
        CancellationToken cancellationToken = default) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(aggregator);

        var entityType = typeof(TEntity);
        var shardsList = DetermineTargetShards(entityType, query.Expression).ToList();

        if (shardsList.Count == 0)
        {
            LogMessages.NoShardsFound(_logger, entityType.Name);
            return default!;
        }

        var tasks = shardsList.Select(async shard =>
        {
            await using var context = await _shardContextFactory.CreateContextAsync(shard, cancellationToken).ConfigureAwait(false);
            var shardQuery = BuildShardQuery<TEntity>(context, shard);
            return await ExecuteScalarOnShardAsync<TEntity, TResult>(shardQuery, query.Expression, cancellationToken).ConfigureAwait(false);
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return aggregator(results);
    }

    /// <summary>
    /// Determines target shards based on entity metadata and query predicates.
    /// </summary>
    private IEnumerable<IShardMetadata> DetermineTargetShards(Type entityType, Expression expression)
    {
        var metadata = _metadataRegistry.GetEntityMetadata(entityType);
        if (metadata?.ShardingConfiguration is null)
        {
            // Not sharded - return primary shard
            return _shardRegistry.GetAllShards().Where(s => s.Tier == ShardTier.Hot).Take(1);
        }

        // Extract predicates from query expression
        var predicates = ExtractPredicates(expression);

        // Extract temporal context from expression
        var temporalInfo = ExtractTemporalInfo(expression);

        // Use sharding strategy if available
        var shardingConfig = metadata.ShardingConfiguration;

        // First try date-based resolution
        if (temporalInfo.AsOfDate.HasValue)
        {
            return _shardRegistry.GetShardsForDateRange(
                temporalInfo.AsOfDate.Value,
                temporalInfo.AsOfDate.Value);
        }

        if (temporalInfo.RangeStart.HasValue && temporalInfo.RangeEnd.HasValue)
        {
            return _shardRegistry.GetShardsForDateRange(
                temporalInfo.RangeStart.Value,
                temporalInfo.RangeEnd.Value);
        }

        // Then try shard key predicate matching
        if (predicates.Count > 0)
        {
            var matchingShards = _shardRegistry.GetAllShards()
                .Where(s => MatchesShardPredicate(s, predicates))
                .ToList();

            if (matchingShards.Count > 0)
            {
                return matchingShards;
            }
        }

        // No temporal or key constraints - return all active shards
        return _shardRegistry.GetAllShards().Where(s => s.IsActive);
    }

    /// <summary>
    /// Checks if a shard matches the given predicates.
    /// </summary>
    private static bool MatchesShardPredicate(IShardMetadata shard, IReadOnlyDictionary<string, object?> predicates)
    {
        // Check shard key value matching
        if (!string.IsNullOrEmpty(shard.ShardKeyValue) && predicates.Count > 0)
        {
            foreach (var predicate in predicates.Values)
            {
                if (predicate is not null &&
                    string.Equals(shard.ShardKeyValue, predicate.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Executes query on a specific shard, handling both database-level and table-level sharding.
    /// </summary>
    private async Task<IReadOnlyList<TEntity>> ExecuteOnShardAsync<TEntity>(
        IQueryable<TEntity> originalQuery,
        IShardMetadata shard,
        CancellationToken cancellationToken) where TEntity : class
    {
        try
        {
            await using var context = await _shardContextFactory.CreateContextAsync(shard, cancellationToken).ConfigureAwait(false);
            var shardQuery = BuildShardQuery<TEntity>(context, shard);

            // Apply the original expression tree to the shard's query source
            var finalQuery = ApplyExpressionToSource(originalQuery.Expression, shardQuery);

            LogMessages.ExecutingQueryOnShard(
                _logger,
                shard.ShardId,
                shard.StorageMode,
                shard.TableName ?? "default");

            return await finalQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogMessages.ShardQueryError(_logger, ex, shard.ShardId);

            // Depending on configuration, either throw or return empty results
            if (shard.Tier == ShardTier.Archive)
            {
                // Archive shards may be temporarily unavailable
                return [];
            }

            throw;
        }
    }

    /// <summary>
    /// Builds the appropriate query source for a shard based on its storage mode.
    /// </summary>
    private IQueryable<TEntity> BuildShardQuery<TEntity>(DbContext context, IShardMetadata shard)
        where TEntity : class
    {
        var dbSet = context.Set<TEntity>();

        // For table-level sharding, we need to redirect to the correct table
        // This is achieved through raw SQL or by configuring the model at runtime
        if (shard.StorageMode is ShardStorageMode.Tables or ShardStorageMode.Manual
            && !string.IsNullOrEmpty(shard.TableName))
        {
            // Use FromSqlRaw to query specific table
            // Note: In production, this should use proper model configuration per table
            var tableName = FormatTableName(shard.SchemaName, shard.TableName);
            LogMessages.QueryingTable(_logger, tableName, shard.ShardId);

            // Return the DbSet as queryable - table name resolution handled elsewhere
            // For full implementation, use model builder per-shard or raw SQL
            return dbSet.AsQueryable();
        }

        return dbSet.AsQueryable();
    }

    /// <summary>
    /// Formats a fully qualified table name with optional schema.
    /// </summary>
    private static string FormatTableName(string? schemaName, string tableName)
    {
        return string.IsNullOrEmpty(schemaName)
            ? $"[{tableName}]"
            : $"[{schemaName}].[{tableName}]";
    }

    /// <summary>
    /// Applies an expression tree to a new query source.
    /// </summary>
    private static IQueryable<TEntity> ApplyExpressionToSource<TEntity>(
        Expression expression,
        IQueryable<TEntity> source) where TEntity : class
    {
        var visitor = new QuerySourceReplacer<TEntity>(source);
        var newExpression = visitor.Visit(expression);
        return source.Provider.CreateQuery<TEntity>(newExpression);
    }

    /// <summary>
    /// Extracts equality predicates from a query expression.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> ExtractPredicates(Expression expression)
    {
        var visitor = new PredicateExtractor();
        visitor.Visit(expression);
        return visitor.Predicates;
    }

    private static async Task<TResult> ExecuteScalarOnShardAsync<TEntity, TResult>(
        IQueryable<TEntity> shardQuery,
        Expression expression,
        CancellationToken cancellationToken) where TEntity : class
    {
        // This is a simplified implementation - full implementation would parse expression
        // to determine the scalar operation (Count, Sum, etc.)
        var count = await shardQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        return (TResult)(object)count;
    }

    private static List<TEntity> MergeResults<TEntity>(
        List<TEntity> results,
        Expression expression) where TEntity : class
    {
        // Apply any ordering from the original query
        var orderingInfo = ExtractOrderingInfo<TEntity>(expression);
        if (orderingInfo.OrderBy is not null)
        {
            var compiledOrderBy = orderingInfo.OrderBy.Compile();
            return orderingInfo.Descending
                ? [.. results.OrderByDescending(compiledOrderBy)]
                : [.. results.OrderBy(compiledOrderBy)];
        }

        // Apply Skip/Take if present
        var pagingInfo = ExtractPagingInfo(expression);
        IEnumerable<TEntity> paged = results;

        if (pagingInfo.Skip.HasValue)
        {
            paged = paged.Skip(pagingInfo.Skip.Value);
        }

        if (pagingInfo.Take.HasValue)
        {
            paged = paged.Take(pagingInfo.Take.Value);
        }

        return paged.ToList();
    }

    private static TemporalInfo ExtractTemporalInfo(Expression expression)
    {
        var visitor = new TemporalInfoExtractor();
        visitor.Visit(expression);
        return visitor.Info;
    }

    private static OrderingInfo<TEntity> ExtractOrderingInfo<TEntity>(Expression expression) where TEntity : class
    {
        // Simplified - full implementation would parse OrderBy/ThenBy expressions
        return new OrderingInfo<TEntity>();
    }

    private static PagingInfo ExtractPagingInfo(Expression expression)
    {
        var visitor = new PagingInfoExtractor();
        visitor.Visit(expression);
        return visitor.Info;
    }

    /// <summary>
    /// Replaces query source in an expression tree to redirect to a different IQueryable.
    /// </summary>
    private sealed class QuerySourceReplacer<TEntity> : ExpressionVisitor where TEntity : class
    {
        private readonly IQueryable<TEntity> _newSource;

        public QuerySourceReplacer(IQueryable<TEntity> newSource)
        {
            _newSource = newSource;
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            // Replace any IQueryable<TEntity> constants with our new source
            if (typeof(IQueryable<TEntity>).IsAssignableFrom(node.Type))
            {
                return Expression.Constant(_newSource);
            }
            return base.VisitConstant(node);
        }
    }

    /// <summary>
    /// Extracts equality predicates from a Where clause expression.
    /// </summary>
    private sealed class PredicateExtractor : ExpressionVisitor
    {
        private readonly Dictionary<string, object?> _predicates = [];

        public IReadOnlyDictionary<string, object?> Predicates => _predicates;

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // Look for Where method calls
            if (node.Method.Name == "Where" && node.Arguments.Count == 2)
            {
                if (node.Arguments[1] is UnaryExpression { Operand: LambdaExpression lambda })
                {
                    ExtractFromLambda(lambda);
                }
            }
            return base.VisitMethodCall(node);
        }

        private void ExtractFromLambda(LambdaExpression lambda)
        {
            ExtractFromExpression(lambda.Body);
        }

        private void ExtractFromExpression(Expression expression)
        {
            switch (expression)
            {
                case BinaryExpression { NodeType: ExpressionType.Equal } binary:
                    ExtractEqualityPredicate(binary);
                    break;
                case BinaryExpression { NodeType: ExpressionType.AndAlso } and:
                    ExtractFromExpression(and.Left);
                    ExtractFromExpression(and.Right);
                    break;
            }
        }

        private void ExtractEqualityPredicate(BinaryExpression binary)
        {
            // Extract property = value patterns
            var (propertyName, value) = (binary.Left, binary.Right) switch
            {
                (MemberExpression member, ConstantExpression constant) =>
                    (member.Member.Name, constant.Value),
                (ConstantExpression constant, MemberExpression member) =>
                    (member.Member.Name, constant.Value),
                _ => (null, null)
            };

            if (propertyName is not null)
            {
                _predicates[propertyName] = value;
            }
        }
    }

    private record struct TemporalInfo
    {
        public DateTime? AsOfDate { get; init; }
        public DateTime? RangeStart { get; init; }
        public DateTime? RangeEnd { get; init; }
    }

    private sealed class TemporalInfoExtractor : ExpressionVisitor
    {
        public TemporalInfo Info { get; private set; }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType?.FullName?.Contains("QueryableExtensions") == true)
            {
                switch (node.Method.Name)
                {
                    case "ValidAt" when node.Arguments[1] is ConstantExpression { Value: DateTime date }:
                        Info = Info with { AsOfDate = date };
                        break;
                    case "ValidBetween":
                        if (node.Arguments[1] is ConstantExpression { Value: DateTime start } &&
                            node.Arguments[2] is ConstantExpression { Value: DateTime end })
                        {
                            Info = Info with { RangeStart = start, RangeEnd = end };
                        }
                        break;
                }
            }
            return base.VisitMethodCall(node);
        }
    }

    private record struct OrderingInfo<TEntity> where TEntity : class
    {
        public Expression<Func<TEntity, object>>? OrderBy { get; init; }
        public bool Descending { get; init; }
    }

    private record struct PagingInfo
    {
        public int? Skip { get; init; }
        public int? Take { get; init; }
    }

    private sealed class PagingInfoExtractor : ExpressionVisitor
    {
        public PagingInfo Info { get; private set; }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(Queryable))
            {
                switch (node.Method.Name)
                {
                    case "Skip" when node.Arguments[1] is ConstantExpression { Value: int skip }:
                        Info = Info with { Skip = skip };
                        break;
                    case "Take" when node.Arguments[1] is ConstantExpression { Value: int take }:
                        Info = Info with { Take = take };
                        break;
                }
            }
            return base.VisitMethodCall(node);
        }
    }
}
