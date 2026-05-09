using System.Linq.Expressions;

using Dtde.Abstractions.Metadata;

namespace Dtde.Abstractions.Temporal;

/// <summary>
/// Describes how an entity participates in DTDE's bi-temporal versioning: which
/// properties carry the validity boundaries, whether validity is open-ended, and
/// how to translate a point-in-time query against those boundaries.
/// </summary>
/// <remarks>
/// Property names are user-defined; DTDE makes no assumption about
/// <c>ValidFrom</c>/<c>ValidTo</c> naming and works equally well with
/// <c>EffectiveDate</c>/<c>ExpirationDate</c>, <c>StartDate</c>/<c>EndDate</c>, or
/// any custom convention.
/// </remarks>
public interface ITemporalConfiguration
{
    /// <summary>
    /// Gets the property that marks the start of the validity period.
    /// </summary>
    public IPropertyMetadata ValidFromProperty { get; }

    /// <summary>
    /// Gets the property that marks the end of the validity period, or
    /// <see langword="null"/> when the entity uses open-ended validity.
    /// </summary>
    public IPropertyMetadata? ValidToProperty { get; }

    /// <summary>
    /// Gets a value indicating whether the configuration supports open-ended validity.
    /// When <see langword="true"/>, entities with a null <see cref="ValidToProperty"/>
    /// value are considered perpetually valid.
    /// </summary>
    public bool IsOpenEnded { get; }

    /// <summary>
    /// Gets the sentinel used to represent "valid forever" when an entity's
    /// <see cref="ValidToProperty"/> is null. Typically <see cref="DateTime.MaxValue"/>.
    /// </summary>
    public DateTime OpenEndedValue { get; }

    /// <summary>
    /// Builds a predicate that filters <typeparamref name="TEntity"/> rows valid at the
    /// supplied point in time.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="pointInTime">The instant to evaluate validity at.</param>
    /// <returns>An expression that returns <see langword="true"/> for rows valid at <paramref name="pointInTime"/>.</returns>
    /// <example>
    /// Generated expression for a closed-period entity:
    /// <code>e =&gt; e.ValidFrom &lt;= pointInTime &amp;&amp; e.ValidTo &gt; pointInTime</code>
    /// Generated expression for an open-ended entity:
    /// <code>e =&gt; e.StartDate &lt;= pointInTime &amp;&amp; (e.EndDate == null || e.EndDate &gt; pointInTime)</code>
    /// </example>
    public Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(DateTime pointInTime);
}
