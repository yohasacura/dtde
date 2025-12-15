using System.Linq.Expressions;

namespace Dtde.Abstractions.Metadata;

/// <summary>
/// Configuration for temporal validity properties.
/// Property names are fully configurable - no assumptions about ValidFrom/ValidTo naming.
/// </summary>
/// <remarks>
/// This interface enables property-agnostic temporal configuration, allowing customers
/// to use any DateTime properties as validity boundaries (e.g., EffectiveDate/ExpirationDate,
/// StartDate/EndDate, or any custom naming convention).
/// </remarks>
public interface IValidityConfiguration
{
    /// <summary>
    /// Gets the property representing the start of the validity period.
    /// </summary>
    IPropertyMetadata ValidFromProperty { get; }

    /// <summary>
    /// Gets the optional property representing the end of the validity period.
    /// Null indicates open-ended validity (perpetual until explicitly closed).
    /// </summary>
    IPropertyMetadata? ValidToProperty { get; }

    /// <summary>
    /// Gets whether this configuration supports open-ended validity.
    /// When true, entities with null ValidTo values are considered perpetually valid.
    /// </summary>
    bool IsOpenEnded { get; }

    /// <summary>
    /// Gets the default value used for open-ended validity end dates.
    /// Typically DateTime.MaxValue for representing "forever".
    /// </summary>
    DateTime OpenEndedValue { get; }

    /// <summary>
    /// Builds a predicate expression that filters entities by validity at a specific point in time.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="temporalContext">The point in time to filter against.</param>
    /// <returns>An expression that returns true for entities valid at the temporal context.</returns>
    /// <example>
    /// <code>
    /// // Generated expression for ValidFrom/ValidTo configuration:
    /// // e => e.ValidFrom &lt;= temporalContext &amp;&amp; e.ValidTo &gt; temporalContext
    /// 
    /// // Generated expression for open-ended configuration:
    /// // e => e.StartDate &lt;= temporalContext &amp;&amp; (e.EndDate == null || e.EndDate &gt; temporalContext)
    /// </code>
    /// </example>
    Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(DateTime temporalContext);
}
