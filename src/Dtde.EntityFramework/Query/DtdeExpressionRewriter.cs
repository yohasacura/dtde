using System.Linq.Expressions;
using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Temporal;
using Dtde.EntityFramework.Extensions;

namespace Dtde.EntityFramework.Query;

/// <summary>
/// Rewrites LINQ expressions to inject temporal predicates based on entity metadata.
/// </summary>
public sealed class DtdeExpressionRewriter : ExpressionVisitor, IExpressionRewriter
{
    private readonly IMetadataRegistry _metadataRegistry;
    private readonly ITemporalContext _temporalContext;
    private bool _skipTemporalFiltering;
    private DateTime? _asOfDate;
    private DateTime? _rangeStart;
    private DateTime? _rangeEnd;
    private string? _targetShardId;

    /// <summary>
    /// Initializes a new instance of the <see cref="DtdeExpressionRewriter"/> class.
    /// </summary>
    /// <param name="metadataRegistry">The metadata registry.</param>
    /// <param name="temporalContext">The temporal context.</param>
    public DtdeExpressionRewriter(
        IMetadataRegistry metadataRegistry,
        ITemporalContext temporalContext)
    {
        _metadataRegistry = metadataRegistry ?? throw new ArgumentNullException(nameof(metadataRegistry));
        _temporalContext = temporalContext ?? throw new ArgumentNullException(nameof(temporalContext));
    }

    /// <inheritdoc />
    public Expression Rewrite(Expression expression)
    {
        // Reset state
        _skipTemporalFiltering = false;
        _asOfDate = null;
        _rangeStart = null;
        _rangeEnd = null;
        _targetShardId = null;

        // First pass: detect DTDE method calls and extract parameters
        var analyzed = Visit(expression);

        // If no explicit temporal filtering and context has default, apply it
        if (!_skipTemporalFiltering && _asOfDate is null && _rangeStart is null)
        {
            _asOfDate = _temporalContext.CurrentPoint;
        }

        // Second pass: inject temporal predicates
        return InjectTemporalPredicates(analyzed);
    }

    /// <inheritdoc />
    public bool ContainsDtdeMethods(Expression expression)
    {
        var visitor = new DtdeMethodDetector();
        visitor.Visit(expression);
        return visitor.Found;
    }

    /// <inheritdoc />
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        ArgumentNullException.ThrowIfNull(node);

        // Handle DTDE extension methods
        if (node.Method.DeclaringType == typeof(QueryableExtensions))
        {
            return HandleDtdeMethod(node);
        }

        return base.VisitMethodCall(node);
    }

    private Expression HandleDtdeMethod(MethodCallExpression node)
    {
        return node.Method.Name switch
        {
            nameof(QueryableExtensions.ValidAt) => HandleValidAt(node),
            nameof(QueryableExtensions.ValidBetween) => HandleValidBetween(node),
            nameof(QueryableExtensions.WithVersions) => HandleWithVersions(node),
            nameof(QueryableExtensions.ShardHint) => HandleShardHint(node),
            _ => base.VisitMethodCall(node)
        };
    }

    private Expression HandleValidAt(MethodCallExpression node)
    {
        if (node.Arguments[1] is ConstantExpression constant && constant.Value is DateTime date)
        {
            _asOfDate = date;
        }
        // Return the source without the ValidAt call
        return Visit(node.Arguments[0]);
    }

    private Expression HandleValidBetween(MethodCallExpression node)
    {
        if (node.Arguments[1] is ConstantExpression start && start.Value is DateTime startDate &&
            node.Arguments[2] is ConstantExpression end && end.Value is DateTime endDate)
        {
            _rangeStart = startDate;
            _rangeEnd = endDate;
        }
        return Visit(node.Arguments[0]);
    }

    private Expression HandleWithVersions(MethodCallExpression node)
    {
        _skipTemporalFiltering = true;
        return Visit(node.Arguments[0]);
    }

    private Expression HandleShardHint(MethodCallExpression node)
    {
        if (node.Arguments[1] is ConstantExpression constant && constant.Value is string shardId)
        {
            _targetShardId = shardId;
        }
        return Visit(node.Arguments[0]);
    }

    private Expression InjectTemporalPredicates(Expression expression)
    {
        if (_skipTemporalFiltering)
        {
            return expression;
        }

        return new TemporalPredicateInjector(
            _metadataRegistry,
            _asOfDate,
            _rangeStart,
            _rangeEnd).Visit(expression);
    }

    private sealed class DtdeMethodDetector : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(QueryableExtensions))
            {
                Found = true;
            }
            return base.VisitMethodCall(node);
        }
    }

    private sealed class TemporalPredicateInjector : ExpressionVisitor
    {
        private readonly IMetadataRegistry _metadataRegistry;
        private readonly DateTime? _asOfDate;
        private readonly DateTime? _rangeStart;
        private readonly DateTime? _rangeEnd;

        public TemporalPredicateInjector(
            IMetadataRegistry metadataRegistry,
            DateTime? asOfDate,
            DateTime? rangeStart,
            DateTime? rangeEnd)
        {
            _metadataRegistry = metadataRegistry;
            _asOfDate = asOfDate;
            _rangeStart = rangeStart;
            _rangeEnd = rangeEnd;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // Intercept Queryable.Where and inject temporal predicates
            if (node.Method.DeclaringType == typeof(Queryable) &&
                node.Method.Name == "Where" &&
                node.Arguments.Count >= 1)
            {
                var entityType = node.Type.GetGenericArguments().FirstOrDefault();
                if (entityType is not null && TryGetTemporalPredicate(entityType, out var predicate))
                {
                    // Combine existing predicate with temporal predicate
                    var combined = CombinePredicates(node, predicate, entityType);
                    return combined ?? base.VisitMethodCall(node);
                }
            }

            // Also handle calls that operate on IQueryable<T>
            if (IsQueryableMethod(node) && node.Arguments.Count >= 1)
            {
                var source = node.Arguments[0];
                var entityType = GetEntityType(source.Type);

                if (entityType is not null &&
                    TryGetTemporalPredicate(entityType, out var predicate))
                {
                    // Inject Where clause before this method
                    var whereMethod = typeof(Queryable).GetMethods()
                        .First(m => m.Name == "Where" && m.GetParameters().Length == 2)
                        .MakeGenericMethod(entityType);

                    var newSource = Expression.Call(
                        null,
                        whereMethod,
                        Visit(source),
                        predicate);

                    var args = node.Arguments.ToArray();
                    args[0] = newSource;

                    return Expression.Call(
                        node.Object,
                        node.Method,
                        args.Select(a => a == source ? newSource : Visit(a)));
                }
            }

            return base.VisitMethodCall(node);
        }

        private static bool IsQueryableMethod(MethodCallExpression node)
        {
            return node.Method.DeclaringType == typeof(Queryable) &&
                   node.Method.Name is "Select" or "OrderBy" or "OrderByDescending"
                       or "ThenBy" or "ThenByDescending" or "Skip" or "Take"
                       or "First" or "FirstOrDefault" or "Single" or "SingleOrDefault"
                       or "Any" or "Count" or "LongCount" or "ToList";
        }

        private static Type? GetEntityType(Type queryableType)
        {
            if (queryableType.IsGenericType &&
                queryableType.GetGenericTypeDefinition() == typeof(IQueryable<>))
            {
                return queryableType.GetGenericArguments()[0];
            }
            return null;
        }

        private bool TryGetTemporalPredicate(Type entityType, out LambdaExpression predicate)
        {
            predicate = null!;

            var metadata = _metadataRegistry.GetEntityMetadata(entityType);
            if (metadata?.Validity is null)
            {
                return false;
            }

            if (_asOfDate.HasValue)
            {
                var buildPredicateMethod = typeof(IValidityConfiguration)
                    .GetMethod(nameof(IValidityConfiguration.BuildPredicate))!
                    .MakeGenericMethod(entityType);

                predicate = (LambdaExpression)buildPredicateMethod.Invoke(
                    metadata.Validity,
                    [_asOfDate.Value])!;
                return true;
            }

            if (_rangeStart.HasValue && _rangeEnd.HasValue)
            {
                predicate = BuildRangePredicate(metadata.Validity, entityType);
                return true;
            }

            return false;
        }

        private LambdaExpression BuildRangePredicate(
            IValidityConfiguration validityConfig,
            Type entityType)
        {
            var parameter = Expression.Parameter(entityType, "e");

            // ValidFrom <= rangeEnd AND (ValidTo IS NULL OR ValidTo >= rangeStart)
            var validFromProperty = entityType.GetProperty(validityConfig.ValidFromProperty.PropertyName)!;
            var validFromAccess = Expression.Property(parameter, validFromProperty);
            var rangeEndConstant = Expression.Constant(_rangeEnd!.Value);
            var validFromCheck = Expression.LessThanOrEqual(validFromAccess, rangeEndConstant);

            Expression validToCheck;
            if (validityConfig.ValidToProperty is not null)
            {
                var validToProperty = entityType.GetProperty(validityConfig.ValidToProperty.PropertyName)!;
                var validToAccess = Expression.Property(parameter, validToProperty);
                var rangeStartConstant = Expression.Constant(_rangeStart!.Value);

                if (validToProperty.PropertyType == typeof(DateTime?))
                {
                    var hasValue = Expression.Property(validToAccess, "HasValue");
                    var value = Expression.Property(validToAccess, "Value");
                    var nullCheck = Expression.Not(hasValue);
                    var validToGreaterOrEqual = Expression.GreaterThanOrEqual(value, rangeStartConstant);
                    validToCheck = Expression.OrElse(nullCheck, validToGreaterOrEqual);
                }
                else
                {
                    validToCheck = Expression.GreaterThanOrEqual(validToAccess, rangeStartConstant);
                }
            }
            else
            {
                validToCheck = Expression.Constant(true);
            }

            var body = Expression.AndAlso(validFromCheck, validToCheck);
            return Expression.Lambda(body, parameter);
        }

        private static MethodCallExpression? CombinePredicates(
            MethodCallExpression node,
            LambdaExpression temporalPredicate,
            Type entityType)
        {
            if (node.Arguments.Count < 2)
            {
                return null;
            }

            var existingPredicate = node.Arguments[1];
            if (existingPredicate is not UnaryExpression { Operand: LambdaExpression lambda })
            {
                return null;
            }

            // Combine predicates with AND
            var parameter = lambda.Parameters[0];
            var replacedTemporal = new ParameterReplacer(
                temporalPredicate.Parameters[0],
                parameter).Visit(temporalPredicate.Body);

            var combined = Expression.AndAlso(lambda.Body, replacedTemporal);
            var newLambda = Expression.Lambda(combined, parameter);

            return Expression.Call(
                null,
                node.Method,
                node.Arguments[0],
                Expression.Quote(newLambda));
        }
    }

    private sealed class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _oldParam;
        private readonly ParameterExpression _newParam;

        public ParameterReplacer(ParameterExpression oldParam, ParameterExpression newParam)
        {
            _oldParam = oldParam;
            _newParam = newParam;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == _oldParam ? _newParam : base.VisitParameter(node);
        }
    }
}
