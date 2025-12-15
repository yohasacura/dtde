using System.Linq.Expressions;

namespace Dtde.EntityFramework.Query;

/// <summary>
/// Interface for rewriting LINQ expressions to inject temporal predicates.
/// </summary>
public interface IExpressionRewriter
{
    /// <summary>
    /// Rewrites an expression to inject temporal and sharding predicates.
    /// </summary>
    /// <param name="expression">The original expression.</param>
    /// <returns>The rewritten expression with predicates injected.</returns>
    Expression Rewrite(Expression expression);

    /// <summary>
    /// Determines if the expression contains DTDE-specific method calls.
    /// </summary>
    /// <param name="expression">The expression to check.</param>
    /// <returns>True if the expression contains DTDE method calls.</returns>
    bool ContainsDtdeMethods(Expression expression);
}
