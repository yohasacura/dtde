using System.Linq.Expressions;

using Dtde.Abstractions.Metadata;

namespace Dtde.Core.Metadata;

/// <summary>
/// Configuration for temporal validity properties.
/// Supports property-agnostic configuration where customers can use any DateTime properties
/// as validity boundaries.
/// </summary>
public sealed class ValidityConfiguration : IValidityConfiguration
{
    /// <inheritdoc />
    public IPropertyMetadata ValidFromProperty { get; }

    /// <inheritdoc />
    public IPropertyMetadata? ValidToProperty { get; }

    /// <inheritdoc />
    public bool IsOpenEnded => ValidToProperty is null;

    /// <inheritdoc />
    public DateTime OpenEndedValue { get; init; } = DateTime.MaxValue;

    /// <summary>
    /// Creates a validity configuration with the specified properties.
    /// </summary>
    /// <param name="validFromProperty">The property representing validity start.</param>
    /// <param name="validToProperty">Optional property representing validity end.</param>
    public ValidityConfiguration(
        IPropertyMetadata validFromProperty,
        IPropertyMetadata? validToProperty = null)
    {
        ValidFromProperty = validFromProperty ?? throw new ArgumentNullException(nameof(validFromProperty));
        ValidToProperty = validToProperty;

        ValidatePropertyTypes();
    }

    /// <summary>
    /// Creates a validity configuration from property expressions.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="validFromSelector">Expression selecting the validity start property.</param>
    /// <param name="validToSelector">Optional expression selecting the validity end property.</param>
    /// <returns>The configured ValidityConfiguration.</returns>
    public static ValidityConfiguration Create<TEntity>(
        Expression<Func<TEntity, DateTime>> validFromSelector,
        Expression<Func<TEntity, DateTime?>>? validToSelector = null)
    {
        var validFromProperty = PropertyMetadata.FromExpression(validFromSelector);
        var validToProperty = validToSelector is not null
            ? PropertyMetadata.FromExpression(validToSelector)
            : null;

        return new ValidityConfiguration(validFromProperty, validToProperty);
    }

    /// <summary>
    /// Creates a validity configuration from property expressions with non-nullable ValidTo.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="validFromSelector">Expression selecting the validity start property.</param>
    /// <param name="validToSelector">Expression selecting the validity end property.</param>
    /// <returns>The configured ValidityConfiguration.</returns>
    public static ValidityConfiguration Create<TEntity>(
        Expression<Func<TEntity, DateTime>> validFromSelector,
        Expression<Func<TEntity, DateTime>> validToSelector)
    {
        var validFromProperty = PropertyMetadata.FromExpression(validFromSelector);
        var validToProperty = PropertyMetadata.FromExpression(validToSelector);

        return new ValidityConfiguration(validFromProperty, validToProperty);
    }

    /// <summary>
    /// Creates a validity configuration from property names.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="validFromPropertyName">The name of the validity start property.</param>
    /// <param name="validToPropertyName">Optional name of the validity end property.</param>
    /// <returns>The configured ValidityConfiguration.</returns>
    public static ValidityConfiguration Create<TEntity>(
        string validFromPropertyName,
        string? validToPropertyName = null)
    {
        var entityType = typeof(TEntity);
        var validFromProp = entityType.GetProperty(validFromPropertyName)
            ?? throw new ArgumentException($"Property '{validFromPropertyName}' not found on type '{entityType.Name}'.", nameof(validFromPropertyName));

        var validFromProperty = new PropertyMetadata(validFromProp);

        PropertyMetadata? validToProperty = null;
        if (!string.IsNullOrEmpty(validToPropertyName))
        {
            var validToProp = entityType.GetProperty(validToPropertyName)
                ?? throw new ArgumentException($"Property '{validToPropertyName}' not found on type '{entityType.Name}'.", nameof(validToPropertyName));
            validToProperty = new PropertyMetadata(validToProp);
        }

        return new ValidityConfiguration(validFromProperty, validToProperty);
    }

    /// <inheritdoc />
    public Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(DateTime temporalContext)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "e");

        // e.{ValidFromProperty} <= temporalContext
        var validFromProperty = Expression.Property(parameter, ValidFromProperty.PropertyName);
        var dateConstant = Expression.Constant(temporalContext);
        var validFromCondition = Expression.LessThanOrEqual(validFromProperty, dateConstant);

        Expression predicate = validFromCondition;

        if (ValidToProperty is not null)
        {
            var validToProperty = Expression.Property(parameter, ValidToProperty.PropertyName);

            if (ValidToProperty.PropertyType == typeof(DateTime?))
            {
                // e.{ValidToProperty} == null || e.{ValidToProperty} > temporalContext
                var nullConstant = Expression.Constant(null, typeof(DateTime?));
                var isNull = Expression.Equal(validToProperty, nullConstant);
                var valueProperty = Expression.Property(validToProperty, nameof(Nullable<DateTime>.Value));
                var greaterThan = Expression.GreaterThan(valueProperty, dateConstant);
                var validToCondition = Expression.OrElse(isNull, greaterThan);

                predicate = Expression.AndAlso(validFromCondition, validToCondition);
            }
            else
            {
                // e.{ValidToProperty} > temporalContext
                var validToCondition = Expression.GreaterThan(validToProperty, dateConstant);
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
