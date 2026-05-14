using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Temporal;
using Dtde.Abstractions.Transactions;
using Dtde.Core.Transactions;
using Dtde.EntityFramework.Diagnostics;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dtde.EntityFramework.Query;

/// <summary>
/// Executes queries across multiple shards and merges results.
/// Supports both database-level and table-level sharding modes.
/// Per-entity fan-out is scoped to the entity's
/// <see cref="IShardingConfiguration.ShardGroupName">shard group</see>.
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
    private readonly IShardGroupRegistry _shardGroupRegistry;
    private readonly IMetadataRegistry _metadataRegistry;
    private readonly ITemporalContext _temporalContext;
    private readonly IShardContextFactory _shardContextFactory;
    private readonly ICrossShardTransactionCoordinator? _transactionCoordinator;
    private readonly ILogger<ShardedQueryExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShardedQueryExecutor"/> class.
    /// </summary>
    /// <param name="shardRegistry">The flat shard registry.</param>
    /// <param name="shardGroupRegistry">The shard-group registry.</param>
    /// <param name="metadataRegistry">The metadata registry.</param>
    /// <param name="temporalContext">The temporal context.</param>
    /// <param name="shardContextFactory">The shard context factory.</param>
    /// <param name="logger">The logger.</param>
    public ShardedQueryExecutor(
        IShardRegistry shardRegistry,
        IShardGroupRegistry shardGroupRegistry,
        IMetadataRegistry metadataRegistry,
        ITemporalContext temporalContext,
        IShardContextFactory shardContextFactory,
        ILogger<ShardedQueryExecutor> logger)
        : this(shardRegistry, shardGroupRegistry, metadataRegistry, temporalContext, shardContextFactory, transactionCoordinator: null, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance with a cross-shard transaction coordinator.
    /// When an ambient transaction is active, queries reuse each shard's
    /// open participant context — making writes within the transaction
    /// visible to subsequent reads on the same shard (read-after-write).
    /// </summary>
    /// <param name="shardRegistry">The flat shard registry.</param>
    /// <param name="shardGroupRegistry">The shard-group registry.</param>
    /// <param name="metadataRegistry">The metadata registry.</param>
    /// <param name="temporalContext">The temporal context.</param>
    /// <param name="shardContextFactory">The shard context factory.</param>
    /// <param name="transactionCoordinator">The cross-shard transaction coordinator (optional).</param>
    /// <param name="logger">The logger.</param>
    public ShardedQueryExecutor(
        IShardRegistry shardRegistry,
        IShardGroupRegistry shardGroupRegistry,
        IMetadataRegistry metadataRegistry,
        ITemporalContext temporalContext,
        IShardContextFactory shardContextFactory,
        ICrossShardTransactionCoordinator? transactionCoordinator,
        ILogger<ShardedQueryExecutor> logger)
    {
        _shardRegistry = shardRegistry ?? throw new ArgumentNullException(nameof(shardRegistry));
        _shardGroupRegistry = shardGroupRegistry ?? throw new ArgumentNullException(nameof(shardGroupRegistry));
        _metadataRegistry = metadataRegistry ?? throw new ArgumentNullException(nameof(metadataRegistry));
        _temporalContext = temporalContext ?? throw new ArgumentNullException(nameof(temporalContext));
        _shardContextFactory = shardContextFactory ?? throw new ArgumentNullException(nameof(shardContextFactory));
        _transactionCoordinator = transactionCoordinator;
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
            var lease = await GetContextForShardAsync(shard, cancellationToken).ConfigureAwait(false);
            try
            {
                var shardQuery = BuildShardQuery<TEntity>(lease.Context, shard);
                return await ExecuteScalarOnShardAsync<TEntity, TResult>(shardQuery, query.Expression, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (lease.OwnsContext)
                {
                    await lease.Context.DisposeAsync().ConfigureAwait(false);
                }
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return aggregator(results);
    }

    /// <summary>
    /// Determines target shards based on entity metadata and query predicates.
    /// Fan-out is scoped to the entity's shard group — never crosses groups.
    /// </summary>
    private IEnumerable<IShardMetadata> DetermineTargetShards(Type entityType, Expression expression)
    {
        var metadata = _metadataRegistry.GetEntityMetadata(entityType);
        if (metadata?.ShardingConfiguration is null)
        {
            // Not sharded - return primary shard from default group.
            return _shardGroupRegistry.DefaultGroup.Shards
                .Where(s => s.Tier == ShardTier.Hot)
                .Take(1);
        }

        var groupName = metadata.ShardingConfiguration.ShardGroupName;
        // The entity declared a group that wasn't registered. Fail fast here
        // rather than silently fanning out to nothing — the application
        // configuration is wrong.
        var group = _shardGroupRegistry.FindGroup(groupName)
            ?? throw new InvalidOperationException(
                $"Entity '{entityType.Name}' is bound to shard group '{groupName}', but no such " +
                "group is registered. Add it with dtde.AddShardGroup(...) or remove the " +
                "UseShardGroup(...) call on the entity.");

        var groupShards = group.Shards;

        // Extract predicates from query expression
        var predicates = ExtractPredicates(expression);

        // Extract temporal context from expression
        var temporalInfo = ExtractTemporalInfo(expression);

        // First try date-based resolution (within this entity's group only).
        if (temporalInfo.AsOfDate.HasValue)
        {
            return FilterByDateRange(groupShards, temporalInfo.AsOfDate.Value, temporalInfo.AsOfDate.Value);
        }

        if (temporalInfo.RangeStart.HasValue && temporalInfo.RangeEnd.HasValue)
        {
            return FilterByDateRange(groupShards, temporalInfo.RangeStart.Value, temporalInfo.RangeEnd.Value);
        }

        // Then try shard key predicate matching (within this entity's group only).
        if (predicates.Count > 0)
        {
            var matchingShards = groupShards
                .Where(s => MatchesShardPredicate(s, predicates))
                .ToList();

            if (matchingShards.Count > 0)
            {
                return matchingShards;
            }
        }

        // No temporal or key constraints - return all active shards in the group.
        return groupShards.Where(s => s.IsActive);
    }

    private static IEnumerable<IShardMetadata> FilterByDateRange(
        IReadOnlyList<IShardMetadata> shards,
        DateTime startDate,
        DateTime endDate)
    {
        var queryRange = new DateRange(startDate, endDate);
        return shards.Where(s => s.DateRange is null || s.DateRange.Value.Intersects(queryRange));
    }

    /// <summary>
    /// Checks if a shard matches the given predicates.
    /// </summary>
    private static bool MatchesShardPredicate(IShardMetadata shard, IReadOnlyDictionary<string, object?> predicates)
    {
        // Check shard key value matching
        if (string.IsNullOrEmpty(shard.ShardKeyValue) || predicates.Count == 0)
        {
            return false;
        }

        return predicates.Values
            .Where(p => p is not null)
            .Any(p => string.Equals(shard.ShardKeyValue, p!.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Executes query on a specific shard, handling both database-level and table-level sharding.
    /// Reuses the ambient cross-shard transaction's per-shard context when one is active, so
    /// queries see writes made earlier in the same transaction (read-after-write).
    /// </summary>
    private async Task<IReadOnlyList<TEntity>> ExecuteOnShardAsync<TEntity>(
        IQueryable<TEntity> originalQuery,
        IShardMetadata shard,
        CancellationToken cancellationToken) where TEntity : class
    {
        DbContext? freshContext = null;
        try
        {
            var context = await GetContextForShardAsync(shard, cancellationToken).ConfigureAwait(false);
            freshContext = context.OwnsContext ? context.Context : null;

            var shardQuery = BuildShardQuery<TEntity>(context.Context, shard);

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
        finally
        {
            if (freshContext is not null)
            {
                await freshContext.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TEntity> ExecuteStreamingAsync<TEntity>(
        IQueryable<TEntity> query,
        int? bufferSize = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(query);

        var entityType = typeof(TEntity);
        var shardsList = DetermineTargetShards(entityType, query.Expression).ToList();

        if (shardsList.Count == 0)
        {
            LogMessages.NoShardsFound(_logger, entityType.Name);
            yield break;
        }

        // Bounded channel: producers (per-shard streams) push into it,
        // consumer pulls in arrival order. The bound is per-channel — once
        // it's full, producers wait, so memory stays roughly proportional
        // to the bound × entity size, regardless of total result-set size.
        var capacity = Math.Max(16, bufferSize ?? shardsList.Count * 64);
        var channel = Channel.CreateBounded<TEntity>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true,
        });

        using var producerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producerToken = producerCts.Token;

        // Fan out producers — one Task per shard pumping its IAsyncEnumerable
        // into the channel.
        var producerTasks = shardsList
            .Select(shard => Task.Run(() => StreamShardAsync(query, shard, channel.Writer, producerToken), producerToken))
            .ToArray();

        // When all shards finish, complete the channel so the consumer loop
        // terminates. Errors from producers complete the channel with the
        // first exception, which propagates out of the consumer below.
        var completionTask = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(producerTasks).ConfigureAwait(false);
                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
            }
        }, producerToken);

        try
        {
            await foreach (var entity in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return entity;
            }
        }
        finally
        {
            // Tear down producers if the consumer abandoned the stream.
            producerCts.Cancel();
            try
            {
                await completionTask.ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Producers may throw on cancellation; we already yielded to the caller.
            catch
#pragma warning restore CA1031
            {
                // Swallowed — caller's cancellation already propagated.
            }
        }
    }

    private async Task StreamShardAsync<TEntity>(
        IQueryable<TEntity> originalQuery,
        IShardMetadata shard,
        ChannelWriter<TEntity> writer,
        CancellationToken cancellationToken) where TEntity : class
    {
        var lease = await GetContextForShardAsync(shard, cancellationToken).ConfigureAwait(false);
        try
        {
            var shardQuery = BuildShardQuery<TEntity>(lease.Context, shard);
            var finalQuery = ApplyExpressionToSource(originalQuery.Expression, shardQuery);

            await foreach (var entity in finalQuery.AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                await writer.WriteAsync(entity, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (lease.OwnsContext)
            {
                await lease.Context.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private readonly record struct ShardContextLease(DbContext Context, bool OwnsContext);

    /// <summary>
    /// Returns a per-shard <see cref="DbContext"/> for executing a query.
    /// When an ambient cross-shard transaction is active and has a participant
    /// for this shard, the participant's open context is reused (so queries see
    /// uncommitted writes made earlier in the transaction). Otherwise — or when
    /// the transaction has no participant for this shard yet — the shard is
    /// auto-enlisted, so subsequent operations stay transactional. Without an
    /// ambient transaction, a fresh context is materialised and disposed by the
    /// caller.
    /// </summary>
    private async Task<ShardContextLease> GetContextForShardAsync(
        IShardMetadata shard,
        CancellationToken cancellationToken)
    {
        if (_transactionCoordinator?.CurrentTransaction is CrossShardTransaction tx)
        {
            var participant = await tx.GetOrCreateParticipantAsync(shard, cancellationToken).ConfigureAwait(false);
            return new ShardContextLease(participant.Context, OwnsContext: false);
        }

        var fresh = await _shardContextFactory.CreateContextAsync(shard, cancellationToken).ConfigureAwait(false);
        return new ShardContextLease(fresh, OwnsContext: true);
    }

    /// <summary>
    /// Returns the per-shard <see cref="DbSet{TEntity}"/>. The DbContext was
    /// produced by <see cref="PerShardContextFactory{TContext}"/>, which tagged
    /// it with the shard's id; <see cref="Infrastructure.DtdeShardModelCustomizer"/>
    /// then rewrote the entity's table name for that shard — so
    /// <c>context.Set&lt;TEntity&gt;()</c> already maps to <c>Customers_EU</c>
    /// (or whatever the shard's table-name pattern resolved to).
    /// </summary>
    private static IQueryable<TEntity> BuildShardQuery<TEntity>(DbContext context, IShardMetadata shard)
        where TEntity : class
    {
        _ = shard;
        return context.Set<TEntity>().AsQueryable();
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
            if (node.Method.Name == "Where" &&
                node.Arguments.Count == 2 &&
                node.Arguments[1] is UnaryExpression { Operand: LambdaExpression lambda })
            {
                ExtractFromLambda(lambda);
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
            // Find the side that is a member access rooted at the lambda
            // parameter (the entity), then evaluate the other side. Closures
            // (`var r = "EU"; Where(a => a.Region == r)`) compile to
            // MemberExpressions rooted at a hoisted-locals ConstantExpression,
            // not the lambda parameter — without evaluation we'd miss them
            // and fan out to every shard. Mirrors EF Core's own
            // ParameterExtractingExpressionVisitor behaviour, scoped down to
            // the narrow shapes the shard pruner cares about.
            if (TryGetEntityMemberName(binary.Left, out var leftName) &&
                TryEvaluate(binary.Right, out var rightValue))
            {
                _predicates[leftName] = rightValue;
                return;
            }

            if (TryGetEntityMemberName(binary.Right, out var rightName) &&
                TryEvaluate(binary.Left, out var leftValue))
            {
                _predicates[rightName] = leftValue;
            }
        }

        private static bool TryGetEntityMemberName(
            Expression expression,
            [NotNullWhen(true)] out string? name)
        {
            if (expression is MemberExpression member && RootsAtParameter(member))
            {
                name = member.Member.Name;
                return true;
            }

            name = null;
            return false;
        }

        private static bool RootsAtParameter(MemberExpression member)
        {
            // Walk the access chain to its base. `a.X` roots at a
            // ParameterExpression; `closure.r` or `closure.req.X` roots at a
            // ConstantExpression (the hoisted-locals object).
            Expression? current = member.Expression;
            while (current is MemberExpression inner)
            {
                current = inner.Expression;
            }

            return current is ParameterExpression;
        }

        // Only evaluate shapes that are guaranteed stable and side-effect free:
        // constants, and member-access chains rooted at a closure constant.
        // We deliberately do *not* compile-and-invoke arbitrary expressions —
        // a predicate like `Where(a => a.Region == NextRegion())` would let the
        // pruner observe a different value than EF Core does at query time,
        // routing to the wrong shard. Method calls, conditionals, and other
        // computed expressions fall back to fan-out (correctness preserved).
        private static bool TryEvaluate(Expression expression, out object? value)
        {
            expression = UnwrapConversion(expression);

            if (expression is ConstantExpression constant)
            {
                value = constant.Value;
                return true;
            }

            if (expression is MemberExpression member)
            {
                return TryEvaluateClosureMemberAccess(member, out value);
            }

            value = null;
            return false;
        }

        // Strips Convert / ConvertChecked / TypeAs nodes the compiler inserts
        // around captured values when the closure field type doesn't exactly
        // match the comparand type (boxing, nullable lifting, etc.).
        private static Expression UnwrapConversion(Expression expression)
        {
            while (expression is UnaryExpression
                {
                    NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs,
                    Operand: var operand,
                })
            {
                expression = operand;
            }

            return expression;
        }

        // Walks an instance member-access chain by reflection, anchored at a
        // closure constant. Static members (member.Expression is null) and
        // indexed properties are out of scope.
        private static bool TryEvaluateClosureMemberAccess(MemberExpression member, out object? value)
        {
            if (member.Expression is null)
            {
                value = null;
                return false;
            }

            if (!TryEvaluate(member.Expression, out var instance))
            {
                value = null;
                return false;
            }

            try
            {
                switch (member.Member)
                {
                    case FieldInfo field:
                        value = field.GetValue(instance);
                        return true;

                    case PropertyInfo property when property.GetMethod is not null
                                                  && property.GetMethod.GetParameters().Length == 0:
                        value = property.GetValue(instance);
                        return true;

                    default:
                        value = null;
                        return false;
                }
            }
#pragma warning disable CA1031 // Best-effort: a malformed closure or unsupported member shape falls back to fan-out.
            catch
#pragma warning restore CA1031
            {
                value = null;
                return false;
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
