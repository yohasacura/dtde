using System.Linq.Expressions;
using System.Reflection;

using Dtde.Abstractions.Metadata;

namespace Dtde.Core.Metadata;

/// <summary>
/// Implementation of property metadata with compiled getters and setters for fast access.
/// </summary>
public sealed class PropertyMetadata : IPropertyMetadata
{
    private readonly Func<object, object?> _getter;
    private readonly Action<object, object?> _setter;

    /// <inheritdoc />
    public string PropertyName { get; }

    /// <inheritdoc />
    public Type PropertyType { get; }

    /// <inheritdoc />
    public string ColumnName { get; }

    /// <inheritdoc />
    public PropertyInfo PropertyInfo { get; }

    /// <summary>
    /// Creates a new PropertyMetadata for the specified property.
    /// </summary>
    /// <param name="propertyInfo">The PropertyInfo for the property.</param>
    /// <param name="columnName">Optional column name override. Defaults to property name.</param>
    public PropertyMetadata(PropertyInfo propertyInfo, string? columnName = null)
    {
        ArgumentNullException.ThrowIfNull(propertyInfo);

        PropertyInfo = propertyInfo;
        PropertyName = propertyInfo.Name;
        PropertyType = propertyInfo.PropertyType;
        ColumnName = columnName ?? propertyInfo.Name;

        _getter = CreateGetter(propertyInfo);
        _setter = CreateSetter(propertyInfo);
    }

    /// <summary>
    /// Creates PropertyMetadata from a property expression.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="propertySelector">Expression selecting the property.</param>
    /// <param name="columnName">Optional column name override.</param>
    /// <returns>The created PropertyMetadata.</returns>
    public static PropertyMetadata FromExpression<TEntity, TProperty>(
        Expression<Func<TEntity, TProperty>> propertySelector,
        string? columnName = null)
    {
        ArgumentNullException.ThrowIfNull(propertySelector);

        var propertyInfo = ExtractPropertyInfo(propertySelector);
        return new PropertyMetadata(propertyInfo, columnName);
    }

    /// <inheritdoc />
    public object? GetValue(object entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return _getter(entity);
    }

    /// <inheritdoc />
    public void SetValue(object entity, object? value)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _setter(entity, value);
    }

    private static Func<object, object?> CreateGetter(PropertyInfo propertyInfo)
    {
        var entityParam = Expression.Parameter(typeof(object), "entity");
        var castEntity = Expression.Convert(entityParam, propertyInfo.DeclaringType!);
        var propertyAccess = Expression.Property(castEntity, propertyInfo);
        var castResult = Expression.Convert(propertyAccess, typeof(object));

        return Expression.Lambda<Func<object, object?>>(castResult, entityParam).Compile();
    }

    private static Action<object, object?> CreateSetter(PropertyInfo propertyInfo)
    {
        if (!propertyInfo.CanWrite)
        {
            return (_, _) => throw new InvalidOperationException(
                $"Property '{propertyInfo.Name}' on type '{propertyInfo.DeclaringType?.Name}' is read-only.");
        }

        var entityParam = Expression.Parameter(typeof(object), "entity");
        var valueParam = Expression.Parameter(typeof(object), "value");
        var castEntity = Expression.Convert(entityParam, propertyInfo.DeclaringType!);
        var castValue = Expression.Convert(valueParam, propertyInfo.PropertyType);
        var propertyAccess = Expression.Property(castEntity, propertyInfo);
        var assign = Expression.Assign(propertyAccess, castValue);

        return Expression.Lambda<Action<object, object?>>(assign, entityParam, valueParam).Compile();
    }

    private static PropertyInfo ExtractPropertyInfo<TEntity, TProperty>(
        Expression<Func<TEntity, TProperty>> propertySelector)
    {
        if (propertySelector.Body is MemberExpression memberExpression)
        {
            if (memberExpression.Member is PropertyInfo propertyInfo)
            {
                return propertyInfo;
            }
        }

        if (propertySelector.Body is UnaryExpression unaryExpression &&
            unaryExpression.Operand is MemberExpression unaryMemberExpression)
        {
            if (unaryMemberExpression.Member is PropertyInfo propertyInfo)
            {
                return propertyInfo;
            }
        }

        throw new ArgumentException(
            "Expression must be a property accessor.",
            nameof(propertySelector));
    }
}
