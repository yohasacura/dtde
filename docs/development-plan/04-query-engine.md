# DTDE Development Plan - Query Engine

[← Back to EF Core Integration](03-ef-core-integration.md) | [Next: Update Engine →](05-update-engine.md)

---

## 1. Query Engine Overview

The Query Engine is responsible for transforming EF Core LINQ queries into distributed, temporal-aware operations. It intercepts the query pipeline, rewrites expressions to include temporal filters, resolves target shards, executes queries in parallel, and merges results.

### 1.1 Query Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           LINQ Query                                         │
│  db.Products.Where(p => p.Category == "A").ValidAt(date).Take(10)           │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  1. Expression Rewriter                                                      │
│     • Extract temporal context from ValidAt()                                │
│     • Inject validity predicates (configurable property names)               │
│     • Capture query definition for shard planning                            │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  2. Shard Query Planner                                                      │
│     • Analyze predicates for shard key values                                │
│     • Resolve target shards using sharding strategy                          │
│     • Generate per-shard query definitions                                   │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  3. Query Executor                                                           │
│     • Create per-shard DbContext instances                                   │
│     • Execute queries in parallel with bounded concurrency                   │
│     • Collect results with cancellation support                              │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  4. Result Merger                                                            │
│     • Combine results from all shards                                        │
│     • Apply global ordering                                                  │
│     • Apply global pagination (Skip/Take)                                    │
│     • Return unified result set                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Expression Rewriter

### 2.1 Rewriter Interface

```csharp
namespace Dtde.EntityFramework.Query;

/// <summary>
/// Rewrites LINQ expression trees to inject temporal predicates
/// and extract query information for shard planning.
/// </summary>
public interface IExpressionRewriter
{
    /// <summary>
    /// Rewrites the expression tree to include temporal filters.
    /// </summary>
    /// <param name="expression">The original expression.</param>
    /// <param name="temporalContext">The temporal context for filtering.</param>
    /// <returns>The rewritten expression and extracted query definition.</returns>
    ExpressionRewriteResult Rewrite(Expression expression, ITemporalContext temporalContext);
}

/// <summary>
/// Result of expression rewriting.
/// </summary>
public sealed class ExpressionRewriteResult
{
    /// <summary>
    /// Gets the rewritten expression with temporal predicates.
    /// </summary>
    public Expression RewrittenExpression { get; init; } = null!;
    
    /// <summary>
    /// Gets the extracted query definition for shard planning.
    /// </summary>
    public DtdeQueryDefinition QueryDefinition { get; init; } = null!;
    
    /// <summary>
    /// Gets whether temporal filters were applied.
    /// </summary>
    public bool TemporalFiltersApplied { get; init; }
    
    /// <summary>
    /// Gets the entities involved in the query.
    /// </summary>
    public IReadOnlyList<Type> InvolvedEntityTypes { get; init; } = Array.Empty<Type>();
}
```

### 2.2 Expression Rewriter Implementation

```csharp
namespace Dtde.EntityFramework.Query;

/// <summary>
/// Rewrites LINQ expressions to inject temporal validity predicates.
/// Uses configurable property names from entity metadata.
/// </summary>
public sealed class DtdeExpressionRewriter : ExpressionVisitor, IExpressionRewriter
{
    private readonly IMetadataRegistry _metadataRegistry;
    private readonly ILogger<DtdeExpressionRewriter> _logger;
    
    private ITemporalContext? _currentTemporalContext;
    private DateTime? _querySpecificTemporalPoint;
    private bool _includeHistory;
    private readonly List<Type> _involvedEntityTypes = new();
    private readonly Dictionary<string, object?> _extractedPredicates = new();
    
    public DtdeExpressionRewriter(
        IMetadataRegistry metadataRegistry,
        ILogger<DtdeExpressionRewriter> logger)
    {
        _metadataRegistry = metadataRegistry;
        _logger = logger;
    }
    
    public ExpressionRewriteResult Rewrite(Expression expression, ITemporalContext temporalContext)
    {
        _currentTemporalContext = temporalContext;
        _querySpecificTemporalPoint = null;
        _includeHistory = temporalContext.IncludeHistory;
        _involvedEntityTypes.Clear();
        _extractedPredicates.Clear();
        
        // First pass: detect DTDE extension method calls
        var extensionVisitor = new DtdeExtensionMethodVisitor();
        extensionVisitor.Visit(expression);
        
        _querySpecificTemporalPoint = extensionVisitor.TemporalPoint;
        _includeHistory = extensionVisitor.IncludeHistory || _includeHistory;
        
        // Second pass: rewrite with temporal predicates
        var rewritten = Visit(expression);
        
        // Build query definition
        var queryDefinition = new DtdeQueryDefinition
        {
            OriginalExpression = expression,
            EffectiveTemporalPoint = _querySpecificTemporalPoint ?? temporalContext.CurrentPoint,
            IncludeHistory = _includeHistory,
            InvolvedEntityTypes = _involvedEntityTypes.ToList(),
            ExtractedPredicates = new Dictionary<string, object?>(_extractedPredicates),
            ShardHints = extensionVisitor.ShardHints
        };
        
        return new ExpressionRewriteResult
        {
            RewrittenExpression = rewritten,
            QueryDefinition = queryDefinition,
            TemporalFiltersApplied = !_includeHistory && queryDefinition.EffectiveTemporalPoint.HasValue,
            InvolvedEntityTypes = _involvedEntityTypes.ToList()
        };
    }
    
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        // Handle DTDE extension methods
        if (IsDtdeExtensionMethod(node.Method))
        {
            return HandleDtdeExtensionMethod(node);
        }
        
        // Handle queryable source (DbSet<T>)
        if (IsQueryableSource(node))
        {
            return InjectTemporalFilter(node);
        }
        
        // Extract predicates for shard planning
        if (IsWhereMethod(node))
        {
            ExtractPredicates(node);
        }
        
        return base.VisitMethodCall(node);
    }
    
    protected override Expression VisitConstant(ConstantExpression node)
    {
        // Track entity types from DbSet access
        if (node.Type.IsGenericType 
            && node.Type.GetGenericTypeDefinition() == typeof(DbSet<>))
        {
            var entityType = node.Type.GetGenericArguments()[0];
            _involvedEntityTypes.Add(entityType);
            
            var metadata = _metadataRegistry.GetEntityMetadata(entityType);
            
            if (metadata?.IsTemporal == true)
            {
                _logger.LogDebug(
                    "Found temporal entity {EntityType} in query",
                    entityType.Name);
            }
        }
        
        return base.VisitConstant(node);
    }
    
    private Expression HandleDtdeExtensionMethod(MethodCallExpression node)
    {
        var methodName = node.Method.Name;
        
        return methodName switch
        {
            "ValidAt" => HandleValidAt(node),
            "WithVersions" => HandleWithVersions(node),
            "ValidBetween" => HandleValidBetween(node),
            "ShardHint" => HandleShardHint(node),
            _ => base.VisitMethodCall(node)
        };
    }
    
    private Expression HandleValidAt(MethodCallExpression node)
    {
        // Extract the date argument
        if (node.Arguments.Count >= 2 
            && node.Arguments[1] is ConstantExpression dateExpr)
        {
            _querySpecificTemporalPoint = (DateTime)dateExpr.Value!;
            _includeHistory = false;
            
            _logger.LogDebug(
                "ValidAt({Date}) detected in query",
                _querySpecificTemporalPoint);
        }
        
        // Continue visiting the source without the ValidAt call
        // The temporal filter will be injected at the source level
        return Visit(node.Arguments[0]);
    }
    
    private Expression HandleWithVersions(MethodCallExpression node)
    {
        _includeHistory = true;
        _querySpecificTemporalPoint = null;
        
        _logger.LogDebug("WithVersions() detected - including all versions");
        
        return Visit(node.Arguments[0]);
    }
    
    private Expression HandleValidBetween(MethodCallExpression node)
    {
        // Implementation for range queries
        throw new NotImplementedException();
    }
    
    private Expression HandleShardHint(MethodCallExpression node)
    {
        // Shard hints are captured but don't affect expression
        return Visit(node.Arguments[0]);
    }
    
    private Expression InjectTemporalFilter(MethodCallExpression node)
    {
        var entityType = node.Type.GetGenericArguments()[0];
        var metadata = _metadataRegistry.GetEntityMetadata(entityType);
        
        if (metadata?.IsTemporal != true || _includeHistory)
        {
            return base.VisitMethodCall(node);
        }
        
        var temporalPoint = _querySpecificTemporalPoint 
            ?? _currentTemporalContext?.CurrentPoint;
        
        if (temporalPoint is null)
        {
            return base.VisitMethodCall(node);
        }
        
        // Build the temporal predicate using configured property names
        var predicate = BuildTemporalPredicate(entityType, metadata.Validity!, temporalPoint.Value);
        
        // Wrap the source with a Where clause
        var whereMethod = typeof(Queryable)
            .GetMethods()
            .First(m => m.Name == "Where" && m.GetParameters().Length == 2)
            .MakeGenericMethod(entityType);
        
        var source = base.VisitMethodCall(node);
        
        return Expression.Call(null, whereMethod, source, predicate);
    }
    
    private LambdaExpression BuildTemporalPredicate(
        Type entityType,
        ValidityConfiguration validity,
        DateTime temporalPoint)
    {
        var parameter = Expression.Parameter(entityType, "e");
        
        // e.{ValidFromProperty} <= temporalPoint
        var validFromProperty = Expression.Property(
            parameter, 
            validity.ValidFromProperty.PropertyName);
        var dateConstant = Expression.Constant(temporalPoint);
        var validFromCondition = Expression.LessThanOrEqual(validFromProperty, dateConstant);
        
        Expression predicate = validFromCondition;
        
        if (validity.ValidToProperty is not null)
        {
            // e.{ValidToProperty} > temporalPoint
            var validToProperty = Expression.Property(
                parameter, 
                validity.ValidToProperty.PropertyName);
            var validToCondition = Expression.GreaterThan(validToProperty, dateConstant);
            
            predicate = Expression.AndAlso(validFromCondition, validToCondition);
        }
        
        _logger.LogDebug(
            "Built temporal predicate for {EntityType}: {ValidFrom} <= {Date} AND {ValidTo} > {Date}",
            entityType.Name,
            validity.ValidFromProperty.PropertyName,
            temporalPoint,
            validity.ValidToProperty?.PropertyName ?? "(open-ended)");
        
        return Expression.Lambda(predicate, parameter);
    }
    
    private void ExtractPredicates(MethodCallExpression node)
    {
        // Extract equality predicates for shard planning
        if (node.Arguments.Count >= 2 
            && node.Arguments[1] is UnaryExpression { Operand: LambdaExpression lambda })
        {
            var predicateExtractor = new PredicateExtractor(_metadataRegistry);
            var predicates = predicateExtractor.Extract(lambda.Body);
            
            foreach (var (key, value) in predicates)
            {
                _extractedPredicates[key] = value;
            }
        }
    }
    
    private static bool IsDtdeExtensionMethod(MethodInfo method)
    {
        return method.DeclaringType == typeof(QueryableExtensions);
    }
    
    private static bool IsQueryableSource(MethodCallExpression node)
    {
        // Check if this is the root queryable source
        return node.Method.DeclaringType == typeof(Queryable) 
            && node.Arguments.Count > 0
            && node.Arguments[0].Type.IsGenericType
            && typeof(IQueryable<>).IsAssignableFrom(
                node.Arguments[0].Type.GetGenericTypeDefinition());
    }
    
    private static bool IsWhereMethod(MethodCallExpression node)
    {
        return node.Method.Name == "Where" 
            && node.Method.DeclaringType == typeof(Queryable);
    }
}
```

### 2.3 Query Definition

```csharp
namespace Dtde.EntityFramework.Query;

/// <summary>
/// Captures all information needed to plan and execute a distributed query.
/// </summary>
public sealed class DtdeQueryDefinition
{
    /// <summary>
    /// Gets the original LINQ expression before rewriting.
    /// </summary>
    public Expression OriginalExpression { get; init; } = null!;
    
    /// <summary>
    /// Gets the effective temporal point for filtering.
    /// </summary>
    public DateTime? EffectiveTemporalPoint { get; init; }
    
    /// <summary>
    /// Gets whether historical versions should be included.
    /// </summary>
    public bool IncludeHistory { get; init; }
    
    /// <summary>
    /// Gets the entity types involved in the query.
    /// </summary>
    public IReadOnlyList<Type> InvolvedEntityTypes { get; init; } = Array.Empty<Type>();
    
    /// <summary>
    /// Gets predicates extracted from Where clauses for shard planning.
    /// Key is "EntityType.PropertyName", value is the constant value.
    /// </summary>
    public IReadOnlyDictionary<string, object?> ExtractedPredicates { get; init; } 
        = new Dictionary<string, object?>();
    
    /// <summary>
    /// Gets explicit shard hints from the query.
    /// </summary>
    public IReadOnlyList<string>? ShardHints { get; init; }
    
    /// <summary>
    /// Gets ordering specifications.
    /// </summary>
    public IReadOnlyList<OrderingSpec> Orderings { get; init; } = Array.Empty<OrderingSpec>();
    
    /// <summary>
    /// Gets the Skip value for pagination.
    /// </summary>
    public int? Skip { get; init; }
    
    /// <summary>
    /// Gets the Take value for pagination.
    /// </summary>
    public int? Take { get; init; }
}

/// <summary>
/// Specification for query ordering.
/// </summary>
public sealed class OrderingSpec
{
    public string PropertyName { get; init; } = null!;
    public bool Descending { get; init; }
}
```

---

## 3. Shard Query Planner

### 3.1 Planner Interface

```csharp
namespace Dtde.EntityFramework.Query;

/// <summary>
/// Plans query execution across shards.
/// </summary>
public interface IShardQueryPlanner
{
    /// <summary>
    /// Creates an execution plan for the query.
    /// </summary>
    /// <param name="queryDefinition">The query definition.</param>
    /// <returns>The execution plan with per-shard queries.</returns>
    ShardQueryPlan CreatePlan(DtdeQueryDefinition queryDefinition);
}

/// <summary>
/// Execution plan for a distributed query.
/// </summary>
public sealed class ShardQueryPlan
{
    /// <summary>
    /// Gets the per-shard query specifications.
    /// </summary>
    public IReadOnlyList<ShardQuery> ShardQueries { get; init; } = Array.Empty<ShardQuery>();
    
    /// <summary>
    /// Gets whether global ordering is required after merge.
    /// </summary>
    public bool RequiresGlobalOrdering { get; init; }
    
    /// <summary>
    /// Gets whether global pagination is required after merge.
    /// </summary>
    public bool RequiresGlobalPagination { get; init; }
    
    /// <summary>
    /// Gets the global ordering specifications.
    /// </summary>
    public IReadOnlyList<OrderingSpec> GlobalOrderings { get; init; } = Array.Empty<OrderingSpec>();
    
    /// <summary>
    /// Gets the global Skip value.
    /// </summary>
    public int? GlobalSkip { get; init; }
    
    /// <summary>
    /// Gets the global Take value.
    /// </summary>
    public int? GlobalTake { get; init; }
    
    /// <summary>
    /// Gets the total number of shards to query.
    /// </summary>
    public int TotalShards => ShardQueries.Count;
}

/// <summary>
/// Query specification for a single shard.
/// </summary>
public sealed class ShardQuery
{
    /// <summary>
    /// Gets the target shard metadata.
    /// </summary>
    public ShardMetadata Shard { get; init; } = null!;
    
    /// <summary>
    /// Gets the expression to execute against this shard.
    /// </summary>
    public Expression Expression { get; init; } = null!;
    
    /// <summary>
    /// Gets the entity type being queried.
    /// </summary>
    public Type EntityType { get; init; } = null!;
    
    /// <summary>
    /// Gets the per-shard Take limit (for optimization).
    /// </summary>
    public int? PerShardTake { get; init; }
}
```

### 3.2 Planner Implementation

```csharp
namespace Dtde.EntityFramework.Query;

/// <summary>
/// Plans distributed query execution across shards.
/// </summary>
public sealed class ShardQueryPlanner : IShardQueryPlanner
{
    private readonly IMetadataRegistry _metadataRegistry;
    private readonly ILogger<ShardQueryPlanner> _logger;
    
    public ShardQueryPlanner(
        IMetadataRegistry metadataRegistry,
        ILogger<ShardQueryPlanner> logger)
    {
        _metadataRegistry = metadataRegistry;
        _logger = logger;
    }
    
    public ShardQueryPlan CreatePlan(DtdeQueryDefinition queryDefinition)
    {
        var stopwatch = Stopwatch.StartNew();
        
        // Resolve target shards for each involved entity type
        var allTargetShards = new HashSet<ShardMetadata>();
        
        foreach (var entityType in queryDefinition.InvolvedEntityTypes)
        {
            var entityMetadata = _metadataRegistry.GetEntityMetadata(entityType);
            
            if (entityMetadata?.IsSharded != true)
            {
                // Non-sharded entity - use default shard or all shards
                var defaultShards = _metadataRegistry.ShardRegistry.GetAllShards();
                
                foreach (var shard in defaultShards)
                {
                    allTargetShards.Add(shard);
                }
                
                continue;
            }
            
            // Use sharding strategy to resolve target shards
            var strategy = entityMetadata.Sharding!.Strategy;
            var predicates = ExtractEntityPredicates(queryDefinition, entityType);
            
            var resolvedShards = strategy.ResolveShards(
                entityMetadata,
                _metadataRegistry.ShardRegistry,
                predicates,
                queryDefinition.EffectiveTemporalPoint);
            
            foreach (var shard in resolvedShards)
            {
                allTargetShards.Add(shard);
            }
        }
        
        // Apply shard hints if present
        if (queryDefinition.ShardHints is { Count: > 0 })
        {
            allTargetShards = allTargetShards
                .Where(s => queryDefinition.ShardHints.Contains(s.ShardId))
                .ToHashSet();
            
            _logger.LogDebug(
                "Applied shard hints, reduced to {Count} shards",
                allTargetShards.Count);
        }
        
        // Build per-shard queries
        var shardQueries = allTargetShards
            .OrderBy(s => s.Priority)
            .ThenBy(s => s.Tier)
            .Select(shard => BuildShardQuery(shard, queryDefinition))
            .ToList();
        
        // Determine if global operations are needed
        var requiresGlobalOrdering = queryDefinition.Orderings.Count > 0 && shardQueries.Count > 1;
        var requiresGlobalPagination = (queryDefinition.Skip.HasValue || queryDefinition.Take.HasValue) 
            && shardQueries.Count > 1;
        
        stopwatch.Stop();
        
        _logger.LogInformation(
            "Query plan created: {ShardCount} shards, GlobalOrder={Order}, GlobalPage={Page}, Duration={Duration}ms",
            shardQueries.Count,
            requiresGlobalOrdering,
            requiresGlobalPagination,
            stopwatch.ElapsedMilliseconds);
        
        return new ShardQueryPlan
        {
            ShardQueries = shardQueries,
            RequiresGlobalOrdering = requiresGlobalOrdering,
            RequiresGlobalPagination = requiresGlobalPagination,
            GlobalOrderings = queryDefinition.Orderings,
            GlobalSkip = requiresGlobalPagination ? queryDefinition.Skip : null,
            GlobalTake = requiresGlobalPagination ? queryDefinition.Take : null
        };
    }
    
    private ShardQuery BuildShardQuery(
        ShardMetadata shard,
        DtdeQueryDefinition queryDefinition)
    {
        // Calculate per-shard take for optimization
        // If we need Skip(10).Take(5) globally, each shard should return up to 15 rows
        int? perShardTake = null;
        
        if (queryDefinition.Skip.HasValue || queryDefinition.Take.HasValue)
        {
            perShardTake = (queryDefinition.Skip ?? 0) + (queryDefinition.Take ?? 0);
        }
        
        return new ShardQuery
        {
            Shard = shard,
            Expression = BuildShardExpression(queryDefinition, perShardTake),
            EntityType = queryDefinition.InvolvedEntityTypes.FirstOrDefault() ?? typeof(object),
            PerShardTake = perShardTake
        };
    }
    
    private Expression BuildShardExpression(
        DtdeQueryDefinition queryDefinition,
        int? perShardTake)
    {
        var expression = queryDefinition.OriginalExpression;
        
        // For multi-shard queries with pagination:
        // - Keep ordering in per-shard query (for partial sorting)
        // - Replace Skip/Take with just Take(skip + take)
        if (perShardTake.HasValue)
        {
            expression = ReplacePagination(expression, skip: null, take: perShardTake);
        }
        
        return expression;
    }
    
    private Expression ReplacePagination(Expression expression, int? skip, int? take)
    {
        // Implementation replaces Skip/Take calls in the expression tree
        throw new NotImplementedException();
    }
    
    private IReadOnlyDictionary<string, object?> ExtractEntityPredicates(
        DtdeQueryDefinition queryDefinition,
        Type entityType)
    {
        var prefix = $"{entityType.Name}.";
        
        return queryDefinition.ExtractedPredicates
            .Where(kvp => kvp.Key.StartsWith(prefix))
            .ToDictionary(
                kvp => kvp.Key.Substring(prefix.Length),
                kvp => kvp.Value);
    }
}
```

---

## 4. Query Executor

### 4.1 Executor Interface

```csharp
namespace Dtde.EntityFramework.Query;

/// <summary>
/// Executes distributed queries across shards.
/// </summary>
public interface IDtdeQueryExecutor
{
    /// <summary>
    /// Executes the query plan and returns merged results.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="plan">The query execution plan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The merged query results.</returns>
    Task<IReadOnlyList<TEntity>> ExecuteAsync<TEntity>(
        ShardQueryPlan plan,
        CancellationToken cancellationToken = default)
        where TEntity : class;
    
    /// <summary>
    /// Executes the query plan and returns a count.
    /// </summary>
    /// <param name="plan">The query execution plan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The total count across all shards.</returns>
    Task<int> ExecuteCountAsync(
        ShardQueryPlan plan,
        CancellationToken cancellationToken = default);
}
```

### 4.2 Executor Implementation

```csharp
namespace Dtde.EntityFramework.Query;

/// <summary>
/// Executes distributed queries with bounded parallelism.
/// </summary>
public sealed class DtdeQueryExecutor : IDtdeQueryExecutor
{
    private readonly DtdeOptions _options;
    private readonly IResultMerger _resultMerger;
    private readonly IDbContextFactory<ShardDbContext> _contextFactory;
    private readonly IDtdeDiagnostics _diagnostics;
    private readonly ILogger<DtdeQueryExecutor> _logger;
    
    public DtdeQueryExecutor(
        DtdeOptions options,
        IResultMerger resultMerger,
        IDbContextFactory<ShardDbContext> contextFactory,
        IDtdeDiagnostics diagnostics,
        ILogger<DtdeQueryExecutor> logger)
    {
        _options = options;
        _resultMerger = resultMerger;
        _contextFactory = contextFactory;
        _diagnostics = diagnostics;
        _logger = logger;
    }
    
    public async Task<IReadOnlyList<TEntity>> ExecuteAsync<TEntity>(
        ShardQueryPlan plan,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        var stopwatch = Stopwatch.StartNew();
        
        _logger.LogInformation(
            "[{CorrelationId}] Executing query across {ShardCount} shards",
            correlationId,
            plan.TotalShards);
        
        // Execute queries with bounded parallelism
        var semaphore = new SemaphoreSlim(_options.MaxParallelShards);
        var shardResults = new ConcurrentDictionary<string, ShardQueryResult<TEntity>>();
        
        var tasks = plan.ShardQueries.Select(async shardQuery =>
        {
            await semaphore.WaitAsync(cancellationToken);
            
            try
            {
                var result = await ExecuteShardQueryAsync<TEntity>(
                    shardQuery,
                    correlationId,
                    cancellationToken);
                
                shardResults[shardQuery.Shard.ShardId] = result;
            }
            finally
            {
                semaphore.Release();
            }
        });
        
        await Task.WhenAll(tasks);
        
        // Collect all results
        var allResults = shardResults.Values.ToList();
        var totalRows = allResults.Sum(r => r.Results.Count);
        
        _logger.LogInformation(
            "[{CorrelationId}] Collected {TotalRows} rows from {ShardCount} shards",
            correlationId,
            totalRows,
            allResults.Count);
        
        // Merge results
        var merged = await _resultMerger.MergeAsync(
            allResults.Select(r => r.Results).ToList(),
            plan,
            cancellationToken);
        
        stopwatch.Stop();
        
        // Emit diagnostics
        _diagnostics.EmitQueryExecuted(new QueryExecutedEvent(
            DateTime.UtcNow,
            correlationId,
            typeof(TEntity),
            plan.TotalShards,
            merged.Count,
            stopwatch.Elapsed,
            allResults.ToDictionary(r => r.ShardId, r => r.Duration)));
        
        _logger.LogInformation(
            "[{CorrelationId}] Query completed: {ResultCount} results, {Duration}ms",
            correlationId,
            merged.Count,
            stopwatch.ElapsedMilliseconds);
        
        return merged;
    }
    
    public async Task<int> ExecuteCountAsync(
        ShardQueryPlan plan,
        CancellationToken cancellationToken = default)
    {
        var semaphore = new SemaphoreSlim(_options.MaxParallelShards);
        var counts = new ConcurrentBag<int>();
        
        var tasks = plan.ShardQueries.Select(async shardQuery =>
        {
            await semaphore.WaitAsync(cancellationToken);
            
            try
            {
                var count = await ExecuteShardCountAsync(shardQuery, cancellationToken);
                counts.Add(count);
            }
            finally
            {
                semaphore.Release();
            }
        });
        
        await Task.WhenAll(tasks);
        
        return counts.Sum();
    }
    
    private async Task<ShardQueryResult<TEntity>> ExecuteShardQueryAsync<TEntity>(
        ShardQuery shardQuery,
        string correlationId,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var shardStopwatch = Stopwatch.StartNew();
        
        _logger.LogDebug(
            "[{CorrelationId}] Executing query on shard {ShardId}",
            correlationId,
            shardQuery.Shard.ShardId);
        
        try
        {
            await using var context = CreateShardContext(shardQuery.Shard);
            
            // Build queryable from expression
            var queryable = BuildQueryable<TEntity>(context, shardQuery.Expression);
            
            // Apply per-shard take if specified
            if (shardQuery.PerShardTake.HasValue)
            {
                queryable = queryable.Take(shardQuery.PerShardTake.Value);
            }
            
            var results = await queryable.ToListAsync(cancellationToken);
            
            shardStopwatch.Stop();
            
            _logger.LogDebug(
                "[{CorrelationId}] Shard {ShardId} returned {Count} rows in {Duration}ms",
                correlationId,
                shardQuery.Shard.ShardId,
                results.Count,
                shardStopwatch.ElapsedMilliseconds);
            
            return new ShardQueryResult<TEntity>
            {
                ShardId = shardQuery.Shard.ShardId,
                Results = results,
                Duration = shardStopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[{CorrelationId}] Query failed on shard {ShardId}",
                correlationId,
                shardQuery.Shard.ShardId);
            
            throw new ShardOperationException(
                $"Query failed on shard {shardQuery.Shard.ShardId}: {ex.Message}",
                shardQuery.Shard.ShardId,
                ex);
        }
    }
    
    private async Task<int> ExecuteShardCountAsync(
        ShardQuery shardQuery,
        CancellationToken cancellationToken)
    {
        await using var context = CreateShardContext(shardQuery.Shard);
        
        var queryable = BuildQueryable<object>(context, shardQuery.Expression);
        
        return await queryable.CountAsync(cancellationToken);
    }
    
    private ShardDbContext CreateShardContext(ShardMetadata shard)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ShardDbContext>();
        optionsBuilder.UseSqlServer(shard.ConnectionString);
        
        return new ShardDbContext(optionsBuilder.Options);
    }
    
    private IQueryable<TEntity> BuildQueryable<TEntity>(
        DbContext context,
        Expression expression)
        where TEntity : class
    {
        // Replace the original provider with this context's provider
        var set = context.Set<TEntity>();
        
        var visitor = new QueryProviderReplacer(set.Provider);
        var newExpression = visitor.Visit(expression);
        
        return set.Provider.CreateQuery<TEntity>(newExpression);
    }
}

/// <summary>
/// Result from a single shard query.
/// </summary>
internal sealed class ShardQueryResult<TEntity>
{
    public string ShardId { get; init; } = null!;
    public IReadOnlyList<TEntity> Results { get; init; } = Array.Empty<TEntity>();
    public TimeSpan Duration { get; init; }
}
```

---

## 5. Result Merger

### 5.1 Merger Interface

```csharp
namespace Dtde.EntityFramework.Query;

/// <summary>
/// Merges query results from multiple shards.
/// </summary>
public interface IResultMerger
{
    /// <summary>
    /// Merges results from multiple shards with global ordering and pagination.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="shardResults">Results from each shard.</param>
    /// <param name="plan">The query execution plan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The merged and paginated results.</returns>
    Task<IReadOnlyList<TEntity>> MergeAsync<TEntity>(
        IReadOnlyList<IReadOnlyList<TEntity>> shardResults,
        ShardQueryPlan plan,
        CancellationToken cancellationToken = default);
}
```

### 5.2 Merger Implementation

```csharp
namespace Dtde.EntityFramework.Query;

/// <summary>
/// Merges query results with sorting and pagination.
/// </summary>
public sealed class ResultMerger : IResultMerger
{
    private readonly ILogger<ResultMerger> _logger;
    
    public ResultMerger(ILogger<ResultMerger> logger)
    {
        _logger = logger;
    }
    
    public Task<IReadOnlyList<TEntity>> MergeAsync<TEntity>(
        IReadOnlyList<IReadOnlyList<TEntity>> shardResults,
        ShardQueryPlan plan,
        CancellationToken cancellationToken = default)
    {
        // Concatenate all results
        IEnumerable<TEntity> merged = shardResults.SelectMany(r => r);
        
        // Apply global ordering if required
        if (plan.RequiresGlobalOrdering && plan.GlobalOrderings.Count > 0)
        {
            merged = ApplyOrdering(merged, plan.GlobalOrderings);
            
            _logger.LogDebug(
                "Applied global ordering: {Orderings}",
                string.Join(", ", plan.GlobalOrderings.Select(o => 
                    $"{o.PropertyName} {(o.Descending ? "DESC" : "ASC")}")));
        }
        
        // Apply global pagination if required
        if (plan.RequiresGlobalPagination)
        {
            if (plan.GlobalSkip.HasValue)
            {
                merged = merged.Skip(plan.GlobalSkip.Value);
                
                _logger.LogDebug("Applied global Skip({Skip})", plan.GlobalSkip.Value);
            }
            
            if (plan.GlobalTake.HasValue)
            {
                merged = merged.Take(plan.GlobalTake.Value);
                
                _logger.LogDebug("Applied global Take({Take})", plan.GlobalTake.Value);
            }
        }
        
        IReadOnlyList<TEntity> result = merged.ToList();
        
        return Task.FromResult(result);
    }
    
    private IEnumerable<TEntity> ApplyOrdering<TEntity>(
        IEnumerable<TEntity> source,
        IReadOnlyList<OrderingSpec> orderings)
    {
        if (orderings.Count == 0)
        {
            return source;
        }
        
        IOrderedEnumerable<TEntity>? ordered = null;
        
        foreach (var ordering in orderings)
        {
            var property = typeof(TEntity).GetProperty(ordering.PropertyName)
                ?? throw new InvalidOperationException(
                    $"Property {ordering.PropertyName} not found on {typeof(TEntity).Name}");
            
            Func<TEntity, object?> selector = e => property.GetValue(e);
            
            if (ordered is null)
            {
                ordered = ordering.Descending
                    ? source.OrderByDescending(selector)
                    : source.OrderBy(selector);
            }
            else
            {
                ordered = ordering.Descending
                    ? ordered.ThenByDescending(selector)
                    : ordered.ThenBy(selector);
            }
        }
        
        return ordered ?? source;
    }
}
```

---

## 6. Diagnostics

### 6.1 Diagnostics Interface

```csharp
namespace Dtde.EntityFramework.Diagnostics;

/// <summary>
/// Diagnostics service for DTDE operations.
/// </summary>
public interface IDtdeDiagnostics
{
    /// <summary>
    /// Emits a query executed event.
    /// </summary>
    /// <param name="event">The event data.</param>
    void EmitQueryExecuted(QueryExecutedEvent @event);
    
    /// <summary>
    /// Emits a shard resolved event.
    /// </summary>
    /// <param name="event">The event data.</param>
    void EmitShardResolved(ShardResolvedEvent @event);
    
    /// <summary>
    /// Subscribes to diagnostic events.
    /// </summary>
    /// <param name="observer">The event observer.</param>
    /// <returns>A disposable subscription.</returns>
    IDisposable Subscribe(IDtdeEventObserver observer);
}

/// <summary>
/// Observer for DTDE diagnostic events.
/// </summary>
public interface IDtdeEventObserver
{
    void OnQueryExecuted(QueryExecutedEvent @event);
    void OnShardResolved(ShardResolvedEvent @event);
    void OnVersionCreated(VersionCreatedEvent @event);
    void OnVersionInvalidated(VersionInvalidatedEvent @event);
}
```

---

## 7. Performance Considerations

### 7.1 Query Optimization Strategies

| Strategy | When to Apply | Expected Improvement |
|----------|---------------|----------------------|
| **Single Shard Optimization** | Equality predicate on shard key | Avoid fan-out entirely |
| **Per-Shard Take Limit** | Pagination queries | Reduce data transfer |
| **Shard Priority** | Hot/warm/cold tiers | Query hot shards first |
| **Connection Pooling** | All queries | Reduce connection overhead |
| **Expression Caching** | Repeated query patterns | Reduce expression parsing |

### 7.2 Bounded Parallelism

```csharp
// Configuration for bounded parallelism
public sealed class DtdeOptions
{
    /// <summary>
    /// Maximum concurrent shard queries (default: 10).
    /// </summary>
    public int MaxParallelShards { get; init; } = 10;
    
    /// <summary>
    /// Connection timeout per shard (default: 30s).
    /// </summary>
    public TimeSpan ShardConnectionTimeout { get; init; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// Query timeout per shard (default: 60s).
    /// </summary>
    public TimeSpan ShardQueryTimeout { get; init; } = TimeSpan.FromSeconds(60);
}
```

---

## 8. Test Specifications

Following the `MethodName_Condition_ExpectedResult` pattern:

### 8.1 Expression Rewriter Tests

```csharp
// Rewrite_WithValidAt_InjectsTemporalPredicate
// Rewrite_WithWithVersions_SkipsTemporalFilter
// Rewrite_WithContextTemporalPoint_UsesContextValue
// Rewrite_WithQueryTemporalPoint_OverridesContext
// Rewrite_NonTemporalEntity_NoPredicateInjected
// Rewrite_CustomPropertyNames_UsesConfiguredNames
```

### 8.2 Shard Query Planner Tests

```csharp
// CreatePlan_WithTemporalPoint_ResolvesCorrectShards
// CreatePlan_WithShardKeyPredicate_SingleShardSelected
// CreatePlan_WithNoPredicates_AllShardsSelected
// CreatePlan_WithPagination_SetsPerShardTake
// CreatePlan_WithShardHints_FiltersToHintedShards
```

### 8.3 Query Executor Tests

```csharp
// ExecuteAsync_SingleShard_ReturnsResults
// ExecuteAsync_MultipleShards_MergesResults
// ExecuteAsync_WithCancellation_CancelsGracefully
// ExecuteAsync_ShardFailure_ThrowsShardOperationException
// ExecuteCountAsync_AggregatesFromAllShards
```

### 8.4 Result Merger Tests

```csharp
// MergeAsync_WithOrdering_SortsGlobally
// MergeAsync_WithPagination_AppliesSkipTake
// MergeAsync_NoOrdering_ReturnsAllResults
// MergeAsync_MultipleOrderings_AppliesThenBy
```

---

## Next Steps

Continue to [05 - Update Engine](05-update-engine.md) for temporal versioning and write pipeline details.
