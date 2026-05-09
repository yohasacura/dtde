using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Temporal;

using Dtde.Core.Metadata;

namespace Dtde.Core.Temporal;

/// <summary>
/// Default <see cref="ITemporalConfiguration"/> implementation. Construct directly
/// from <see cref="IPropertyMetadata"/> instances or use the
/// <see cref="Create{TEntity}(Expression{Func{TEntity, DateTime}}, Expression{Func{TEntity, DateTime?}})"/>
/// factory overloads to derive metadata from property selectors / names.
/// </summary>
public sealed class TemporalConfiguration : ITemporalConfiguration
{
    /// <summary>
    /// Creates a configuration from property metadata.
    /// </summary>
    /// <param name="validFromProperty">Property representing validity start.</param>
    /// <param name="validToProperty">Optional property representing validity end.</param>
    /// <exception cref="ArgumentException">Either property is not <see cref="DateTime"/> or <see cref="Nullable{DateTime}"/>.</exception>
    public TemporalConfiguration(
        IPropertyMetadata validFromProperty,
        IPropertyMetadata? validToProperty = null)
    {
        ValidFromProperty = validFromProperty ?? throw new ArgumentNullException(nameof(validFromProperty));
        ValidToProperty = validToProperty;

        ValidatePropertyTypes();
    }

    /// <inheritdoc />
    public IPropertyMetadata ValidFromProperty { get; }

    /// <inheritdoc />
    public IPropertyMetadata? ValidToProperty { get; }

    /// <inheritdoc />
    public bool IsOpenEnded => ValidToProperty is null;

    /// <inheritdoc />
    public DateTime OpenEndedValue { get; init; } = DateTime.MaxValue;

    /// <summary>
    /// Builds a configuration from property selectors with an optional <see cref="Nullable{DateTime}"/> end.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="validFromSelector">Selector for the validity-start property.</param>
    /// <param name="validToSelector">Optional selector for the validity-end property.</param>
    /// <returns>A new <see cref="TemporalConfiguration"/>.</returns>
    public static TemporalConfiguration Create<TEntity>(
        Expression<Func<TEntity, DateTime>> validFromSelector,
        Expression<Func<TEntity, DateTime?>>? validToSelector = null)
    {
        var validFromProperty = PropertyMetadata.FromExpression(validFromSelector);
        var validToProperty = validToSelector is not null
            ? PropertyMetadata.FromExpression(validToSelector)
            : null;

        return new TemporalConfiguration(validFromProperty, validToProperty);
    }

    /// <summary>
    /// Builds a configuration from property selectors with a non-nullable end.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="validFromSelector">Selector for the validity-start property.</param>
    /// <param name="validToSelector">Selector for the validity-end property.</param>
    /// <returns>A new <see cref="TemporalConfiguration"/>.</returns>
    public static TemporalConfiguration Create<TEntity>(
        Expression<Func<TEntity, DateTime>> validFromSelector,
        Expression<Func<TEntity, DateTime>> validToSelector)
    {
        var validFromProperty = PropertyMetadata.FromExpression(validFromSelector);
        var validToProperty = PropertyMetadata.FromExpression(validToSelector);

        return new TemporalConfiguration(validFromProperty, validToProperty);
    }

    /// <summary>
    /// Builds a configuration by reflecting properties of <typeparamref name="TEntity"/> by name.
    /// </summary>
    /// <typeparam name="TEntity">The entity type. Must expose the named properties publicly.</typeparam>
    /// <param name="validFromPropertyName">Name of the validity-start property.</param>
    /// <param name="validToPropertyName">Optional name of the validity-end property.</param>
    /// <returns>A new <see cref="TemporalConfiguration"/>.</returns>
    /// <exception cref="ArgumentException">A named property does not exist on <typeparamref name="TEntity"/>.</exception>
    [RequiresUnreferencedCode("Reflects public properties on TEntity. Pass DynamicallyAccessedMembers=PublicProperties on the caller's TEntity if used in trim/AOT scenarios.")]
    public static TemporalConfiguration Create<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TEntity>(
        string validFromPropertyName,
        string? validToPropertyName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(validFromPropertyName);

        var entityType = typeof(TEntity);
        var validFromProp = entityType.GetProperty(validFromPropertyName)
            ?? throw new ArgumentException(
                $"Property '{validFromPropertyName}' not found on type '{entityType.Name}'.",
                nameof(validFromPropertyName));

        var validFromProperty = new PropertyMetadata(validFromProp);

        PropertyMetadata? validToProperty = null;
        if (!string.IsNullOrEmpty(validToPropertyName))
        {
            var validToProp = entityType.GetProperty(validToPropertyName)
                ?? throw new ArgumentException(
                    $"Property '{validToPropertyName}' not found on type '{entityType.Name}'.",
                    nameof(validToPropertyName));
            validToProperty = new PropertyMetadata(validToProp);
        }

        return new TemporalConfiguration(validFromProperty, validToProperty);
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Builds an Expression that references entity properties by name. Not safe for trimming or AOT.")]
    public Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(DateTime pointInTime)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "e");

        var validFromMember = Expression.Property(parameter, ValidFromProperty.PropertyName);
        var pointConstant = Expression.Constant(pointInTime);
        var validFromCondition = Expression.LessThanOrEqual(validFromMember, pointConstant);

        Expression predicate = validFromCondition;

        if (ValidToProperty is not null)
        {
            var validToMember = Expression.Property(parameter, ValidToProperty.PropertyName);

            if (ValidToProperty.PropertyType == typeof(DateTime?))
            {
                var nullConstant = Expression.Constant(null, typeof(DateTime?));
                var isNull = Expression.Equal(validToMember, nullConstant);
                var valueAccess = Expression.Property(validToMember, nameof(Nullable<DateTime>.Value));
                var afterPoint = Expression.GreaterThan(valueAccess, pointConstant);
                var validToCondition = Expression.OrElse(isNull, afterPoint);

                predicate = Expression.AndAlso(validFromCondition, validToCondition);
            }
            else
            {
                var validToCondition = Expression.GreaterThan(validToMember, pointConstant);
                predicate = Expression.AndAlso(validFromCondition, validToCondition);
            }
        }

        return Expression.Lambda<Func<TEntity, bool>>(predicate, parameter);
    }

    private void ValidatePropertyTypes()
    {
        var validFromType = ValidFromProperty.PropertyType;
        if (validFromType != typeof(DateTime) && validFromType != typeof(DateTime?))
        {
            throw new ArgumentException(
                $"ValidFrom property '{ValidFromProperty.PropertyName}' must be of type DateTime or DateTime?, but was {validFromType.Name}.",
                nameof(ValidFromProperty));
        }

        if (ValidToProperty is not null)
        {
            var validToType = ValidToProperty.PropertyType;
            if (validToType != typeof(DateTime) && validToType != typeof(DateTime?))
            {
                throw new ArgumentException(
                    $"ValidTo property '{ValidToProperty.PropertyName}' must be of type DateTime or DateTime?, but was {validToType.Name}.",
                    nameof(ValidToProperty));
            }
        }
    }
}
